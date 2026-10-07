using System.Collections.Concurrent;
using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

internal sealed class PjManager : IPjManager
{
    private const string LogModule = "Job";

    private readonly List<ProcessJob> _jobs = [];
    public IReadOnlyList<ProcessJob> Jobs => _jobs;

    private readonly ConcurrentDictionary<Guid, string> _owners = new();

    public event Action<ProcessJob, int>? StateChanged;

    public ProcessJob? Find(string id)
    {
        return _jobs.FirstOrDefault(job => string.Equals(job.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public string? OwnerOf(Guid wafer)
    {
        return _owners.TryGetValue(wafer, out string? owner) ? owner : null;
    }

    public void Add(ProcessJob job)
    {
        _jobs.Add(job);
        foreach (var row in job.Rows)
        {
            _owners[row.WaferId] = job.Id;
        }

        // E40 #1：建好进 QUEUED/POOLED，没有出发状态
        Report(job, 1, null, job.State);
    }

    #region E40 状态机

    public bool Fire(ProcessJob job, ProcessStateAction trigger)
    {
        var found = job.IsEnded ? null : Find(job, trigger);
        if (found is null)
        {
            return false;
        }

        var (number, to, ends) = found.Value;
        var from = job.State;
        if (trigger == ProcessStateAction.Pause)
        {
            job.ResumeState = from;
        }

        job.State = to;
        var now = DateTime.Now;
        if (to == ProcessJobState.Processing && job.StartedAt is null)
        {
            job.StartedAt = now;
        }

        if (ends)
        {
            job.EndedBy = number;
            job.EndedAt = now;
            foreach (var row in job.Rows)
            {
                _owners.TryRemove(new KeyValuePair<Guid, string>(row.WaferId, job.Id));
            }

            _jobs.Remove(job);
        }

        Report(job, number, from, ends ? null : to);
        return true;
    }

    /// <summary>
    /// SEMI E40 PJ 状态转换表（#2~#18，号码照标准；#1 是建好时报的）：现在的状态收到这个触发能不能转、转到哪。
    /// Ends 为 true 是转完 PJ 就结束了（标准里的 no state），To 是结束前报的最后状态。
    /// 执行中 = SETTING UP、WAITING FOR START、PROCESSING；暂停 = PAUSING、PAUSED（标准里的超状态）。
    /// </summary>
    private static (int Number, ProcessJobState To, bool Ends)? Find(ProcessJob job, ProcessStateAction trigger)
    {
        var state = job.State;
        bool executing = state == ProcessJobState.SettingUp || state == ProcessJobState.WaitingForStart || state == ProcessJobState.Processing;
        bool paused = state == ProcessJobState.Pausing || state == ProcessJobState.Paused;
        switch (trigger)
        {
            case ProcessStateAction.Setup:
                if (state == ProcessJobState.QueuedPooled)
                {
                    return (2, ProcessJobState.SettingUp, false);
                }

                break;

            case ProcessStateAction.SetupDoneWait:
                if (state == ProcessJobState.SettingUp)
                {
                    return (3, ProcessJobState.WaitingForStart, false);
                }

                break;

            case ProcessStateAction.SetupDoneStart:
                if (state == ProcessJobState.SettingUp)
                {
                    return (4, ProcessJobState.Processing, false);
                }

                break;

            case ProcessStateAction.Start:
                if (state == ProcessJobState.WaitingForStart)
                {
                    return (5, ProcessJobState.Processing, false);
                }

                break;

            case ProcessStateAction.ProcessDone:
                if (state == ProcessJobState.Processing)
                {
                    return (6, ProcessJobState.ProcessComplete, false);
                }

                break;

            case ProcessStateAction.MaterialOut:
                if (state == ProcessJobState.ProcessComplete)
                {
                    return (7, ProcessJobState.ProcessComplete, true);
                }

                break;

            case ProcessStateAction.Pause:
                if (executing)
                {
                    return (8, ProcessJobState.Pausing, false);
                }

                break;

            case ProcessStateAction.PauseDone:
                if (state == ProcessJobState.Pausing)
                {
                    return (9, ProcessJobState.Paused, false);
                }

                break;

            case ProcessStateAction.Resume:
                if (paused)
                {
                    return (10, job.ResumeState, false);
                }

                break;

            case ProcessStateAction.Stop:
                if (executing)
                {
                    return (11, ProcessJobState.Stopping, false);
                }

                if (paused)
                {
                    return (12, ProcessJobState.Stopping, false);
                }

                break;

            case ProcessStateAction.Abort:
                if (executing)
                {
                    return (13, ProcessJobState.Aborting, false);
                }

                if (state == ProcessJobState.Stopping)
                {
                    return (14, ProcessJobState.Aborting, false);
                }

                if (paused)
                {
                    return (15, ProcessJobState.Aborting, false);
                }

                break;

            case ProcessStateAction.AbortDone:
                if (state == ProcessJobState.Aborting)
                {
                    return (16, ProcessJobState.Aborted, true);
                }

                break;

            case ProcessStateAction.StopDone:
                if (state == ProcessJobState.Stopping)
                {
                    return (17, ProcessJobState.Stopped, true);
                }

                break;

            case ProcessStateAction.Dequeue:
                if (state == ProcessJobState.QueuedPooled)
                {
                    return (18, ProcessJobState.QueuedPooled, true);
                }

                break;
        }

        return null;
    }

    public bool Advance()
    {
        bool changed = false;
        foreach (var job in _jobs.ToList())
        {
            var trigger = NextTrigger(job);
            if (trigger is not null && Fire(job, trigger.Value))
            {
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>自动转换：按任务进度，这个 PJ 现在该不该自己往下转、转哪条（不该转返回 null）。</summary>
    private static ProcessStateAction? NextTrigger(ProcessJob job)
    {
        bool idle = !job.HasRowsInMachine && !job.HasRunning;
        switch (job.State)
        {
            case ProcessJobState.QueuedPooled:
                // #2：所属 CJ 让它启动了。一个 PJ 一个 PJ 地投，片在机内的加工照样能叠着做
                if (MayStart(job))
                {
                    return ProcessStateAction.Setup;
                }

                break;

            case ProcessJobState.SettingUp:
                // #3 / #4：片都还在该在的地方（没出错），自动启动的直接开始，手动启动的等 Start
                if (!job.HasErrors)
                {
                    return job.AutoStart ? ProcessStateAction.SetupDoneStart : ProcessStateAction.SetupDoneWait;
                }

                break;

            case ProcessJobState.Processing:
                // #6：片都投了，每片路线上的事都做完了（还在回片路上也算）
                if (job.Rows.All(row => row.IsProcessFinished))
                {
                    return ProcessStateAction.ProcessDone;
                }

                break;

            case ProcessJobState.ProcessComplete:
                // #7：片都回到回片槽，没有在跑的任务
                if (!job.HasRunning && job.Rows.All(row => row.IsReturned))
                {
                    return ProcessStateAction.MaterialOut;
                }

                break;

            case ProcessJobState.Pausing:
                // #9：机内没有这个 PJ 的片了（投出去的都回来了），也没有在跑的
                if (idle)
                {
                    return ProcessStateAction.PauseDone;
                }

                break;

            case ProcessJobState.Stopping:
                // #17：机内的片都走完回片了；出错等人处理的那片也要等它处理完走完
                if (idle)
                {
                    return ProcessStateAction.StopDone;
                }

                break;

            case ProcessJobState.Aborting:
                // #16：在跑的都结束了、发给腔体的中止都做完了、片位都确定（没有出错等处理的、没有保留资源等确认的搬运操作）
                if (!job.HasRunning && job.DeviceAborts.All(abort => abort.IsSettled) && !job.HasErrors && !HasHeldTransfers(job))
                {
                    return ProcessStateAction.AbortDone;
                }

                break;
        }

        return null;
    }

    /// <summary>所属 CJ 让这个 PJ 启动：CJ 在执行、没收 Stop / Abort，CJ 里排在它前面的 PJ 都投完了片（或结束了）。</summary>
    private static bool MayStart(ProcessJob job)
    {
        var control = job.ControlJob;
        if (control is null || !control.CanStartProcessJobs)
        {
            return false;
        }

        foreach (var earlier in control.ProcessJobs)
        {
            if (ReferenceEquals(earlier, job))
            {
                break;
            }

            if (!earlier.IsEnded && earlier.HasWaitingRows)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>这个 PJ 有动过片才失败、资源仍保留等人工确认的搬运操作。</summary>
    private static bool HasHeldTransfers(ProcessJob job)
    {
        return TransferManager.Current?.HeldOperations.Any(operation => string.Equals(operation.Owner, job.Id, StringComparison.Ordinal)) == true;
    }

    /// <summary>
    /// 许可：PROCESSING 投新片、走机内的片；PAUSING、STOPPING、PROCESS COMPLETE 只走机内的片（机内没片了再自己转）；别的状态不动。
    /// </summary>
    public void UpdatePermissions()
    {
        foreach (var job in _jobs)
        {
            TaskPermission permission;
            switch (job.State)
            {
                case ProcessJobState.Processing:
                    permission = TaskPermission.Advance | TaskPermission.Feed;
                    break;

                case ProcessJobState.Pausing:
                case ProcessJobState.Stopping:
                case ProcessJobState.ProcessComplete:
                    permission = TaskPermission.Advance;
                    break;

                default:
                    permission = TaskPermission.None;
                    break;
            }

            foreach (var row in job.Rows)
            {
                row.Permission = permission;
            }
        }
    }

    #endregion

    #region PJ 命令（E40）

    /// <summary>执行 PJ 命令（先查 E40 转换表，表里没有的一律拒，回当前状态）。排队的 PJ 收到 Stop / Abort 就是撤掉（#18）。</summary>
    public HandleResult Execute(string id, ProcessJobCommand command)
    {
        var job = Find(id.Trim());
        if (job is null || job.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id.Trim());
        }

        string name = JobNames.Of(command);
        bool queued = job.State == ProcessJobState.QueuedPooled;
        ProcessStateAction? trigger = null;
        switch (command)
        {
            case ProcessJobCommand.Start:
                trigger = ProcessStateAction.Start;
                break;

            case ProcessJobCommand.Pause:
                trigger = ProcessStateAction.Pause;
                break;

            case ProcessJobCommand.Resume:
                trigger = ProcessStateAction.Resume;
                break;

            case ProcessJobCommand.Stop:
                trigger = queued ? ProcessStateAction.Dequeue : ProcessStateAction.Stop;
                break;

            case ProcessJobCommand.Abort:
                trigger = queued ? ProcessStateAction.Dequeue : ProcessStateAction.Abort;
                break;

            case ProcessJobCommand.Cancel:
                if (queued)
                {
                    trigger = ProcessStateAction.Dequeue;
                }

                break;
        }

        return trigger is not null && Fire(job, trigger.Value)
            ? HandleResult.Success(job.Id)
            : HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
    }

    public void AbortLoose()
    {
        foreach (var job in _jobs.ToList())
        {
            if (!job.IsEnded && job.ControlJob is null)
            {
                Fire(job, job.State == ProcessJobState.QueuedPooled ? ProcessStateAction.Dequeue : ProcessStateAction.Abort);
            }
        }
    }

    #endregion

    /// <summary>PJ 转了（含刚建好的 #1）：记一行日志，告诉 Job 组件。</summary>
    private void Report(ProcessJob job, int number, ProcessJobState? from, ProcessJobState? to)
    {
        LogHelper.Info(LogModule, $"PJ {job.Id} E40 #{number}：{JobNames.Of(from)} → {JobNames.Of(to)}");
        StateChanged?.Invoke(job, number);
    }
}
