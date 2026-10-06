using System.Collections.Concurrent;
using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

internal sealed class PjManager : IPjManager
{
    /// <summary>E40 #1：建好，进 QUEUED/POOLED（没有出发状态，建 PJ 时直接报）。</summary>
    public const int CreatedTransition = 1;

    private const string LogModule = "Job";

    private readonly List<ProcessJob> _jobs = [];
    private readonly ConcurrentDictionary<Guid, string> _owners = new();

    public event Action<ProcessJob, int, PrJobState?, PrJobState?>? Transitioned;

    public IReadOnlyList<ProcessJob> Jobs => _jobs;

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

        Report(job, CreatedTransition, null, job.State);
    }

    #region E40 状态机

    /// <summary>
    /// 转一次：E40 转换表里有才转（<see cref="Find(ProcessJob, ProcessStateAction)"/>）。暂停（#8）记下暂停前的执行子状态，恢复（#10）回到那里；
    /// 进 PROCESSING 记开始时刻；结束了放开片、从队列拿掉（还留在所属 CJ 里给界面看）。最后告诉 Job 组件。
    /// </summary>
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
        if (to == PrJobState.Processing && job.StartedAt is null)
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
    private static (int Number, PrJobState To, bool Ends)? Find(ProcessJob job, ProcessStateAction trigger)
    {
        var state = job.State;
        bool executing = state is PrJobState.SettingUp or PrJobState.WaitingForStart or PrJobState.Processing;
        bool paused = state is PrJobState.Pausing or PrJobState.Paused;
        return trigger switch
        {
            ProcessStateAction.Setup when state == PrJobState.QueuedPooled => (2, PrJobState.SettingUp, false),
            ProcessStateAction.SetupDoneWait when state == PrJobState.SettingUp => (3, PrJobState.WaitingForStart, false),
            ProcessStateAction.SetupDoneStart when state == PrJobState.SettingUp => (4, PrJobState.Processing, false),
            ProcessStateAction.Start when state == PrJobState.WaitingForStart => (5, PrJobState.Processing, false),
            ProcessStateAction.ProcessDone when state == PrJobState.Processing => (6, PrJobState.ProcessComplete, false),
            ProcessStateAction.MaterialOut when state == PrJobState.ProcessComplete => (7, PrJobState.ProcessComplete, true),
            ProcessStateAction.Pause when executing => (8, PrJobState.Pausing, false),
            ProcessStateAction.PauseDone when state == PrJobState.Pausing => (9, PrJobState.Paused, false),
            ProcessStateAction.Resume when paused => (10, job.ResumeState, false),
            ProcessStateAction.Stop when executing => (11, PrJobState.Stopping, false),
            ProcessStateAction.Stop when paused => (12, PrJobState.Stopping, false),
            ProcessStateAction.Abort when executing => (13, PrJobState.Aborting, false),
            ProcessStateAction.Abort when state == PrJobState.Stopping => (14, PrJobState.Aborting, false),
            ProcessStateAction.Abort when paused => (15, PrJobState.Aborting, false),
            ProcessStateAction.AbortDone when state == PrJobState.Aborting => (16, PrJobState.Aborted, true),
            ProcessStateAction.StopDone when state == PrJobState.Stopping => (17, PrJobState.Stopped, true),
            ProcessStateAction.Dequeue when state == PrJobState.QueuedPooled => (18, PrJobState.QueuedPooled, true),
            _ => null,
        };
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
        return job.State switch
        {
            // #2：所属 CJ 让它启动了。一个 PJ 一个 PJ 地投，片在机内的加工照样能叠着做
            PrJobState.QueuedPooled => MayStart(job) ? ProcessStateAction.Setup : null,

            // #3 / #4：片都还在该在的地方（没出错），自动启动的直接开始，手动启动的等 Start
            PrJobState.SettingUp => job.HasErrors ? null : job.AutoStart ? ProcessStateAction.SetupDoneStart : ProcessStateAction.SetupDoneWait,

            // #6：片都投了，每片路线上的事都做完了（还在回片路上也算）
            PrJobState.Processing => job.Rows.All(row => row.IsProcessFinished) ? ProcessStateAction.ProcessDone : null,

            // #7：片都回到回片槽，没有在跑的任务
            PrJobState.ProcessComplete => !job.HasRunning && job.Rows.All(row => row.IsReturned) ? ProcessStateAction.MaterialOut : null,

            // #9：机内没有这个 PJ 的片了（投出去的都回来了），也没有在跑的
            PrJobState.Pausing => idle ? ProcessStateAction.PauseDone : null,

            // #17：机内的片都走完回片了；出错等人处理的那片也要等它处理完走完
            PrJobState.Stopping => idle ? ProcessStateAction.StopDone : null,

            // #16：在跑的都结束了、发给腔体的中止都做完了、片位都确定（没有出错等处理的、没有留着锁等确认的搬运单）
            PrJobState.Aborting => !job.HasRunning && job.DeviceAborts.All(abort => abort.IsSettled) && !job.HasErrors && !HasHeldTransfers(job)
                ? ProcessStateAction.AbortDone
                : null,

            _ => null,
        };
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

    /// <summary>这个 PJ 有碰过片才失败、锁还留着等人确认的搬运单。</summary>
    private static bool HasHeldTransfers(ProcessJob job)
    {
        return TransferManager.Current?.HeldResults.Any(result => string.Equals(result.Owner, job.Id, StringComparison.Ordinal)) == true;
    }

    /// <summary>
    /// 许可：PROCESSING 投新片、走机内的片；PAUSING、STOPPING、PROCESS COMPLETE 只走机内的片（机内没片了再自己转）；别的状态不动。
    /// </summary>
    public void UpdatePermissions()
    {
        foreach (var job in _jobs)
        {
            var permission = job.State switch
            {
                PrJobState.Processing => TaskPermission.Advance | TaskPermission.Feed,
                PrJobState.Pausing or PrJobState.Stopping or PrJobState.ProcessComplete => TaskPermission.Advance,
                _ => TaskPermission.None,
            };
            foreach (var row in job.Rows)
            {
                row.Permission = permission;
            }
        }
    }

    #endregion

    #region PJ 命令（E40）

    /// <summary>PJ 命令（先查 E40 转换表，表里没有的一律拒，回当前状态）。排队的 PJ 收到 Stop / Abort 就是撤掉（#18）。</summary>
    public HandleResult Command(string id, PrJobCommand command)
    {
        var job = Find(id.Trim());
        if (job is null || job.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id.Trim());
        }

        string name = JobNames.Of(command);
        bool queued = job.State == PrJobState.QueuedPooled;
        ProcessStateAction? trigger = command switch
        {
            PrJobCommand.Start => ProcessStateAction.Start,
            PrJobCommand.Pause => ProcessStateAction.Pause,
            PrJobCommand.Resume => ProcessStateAction.Resume,
            PrJobCommand.Stop => queued ? ProcessStateAction.Dequeue : ProcessStateAction.Stop,
            PrJobCommand.Abort => queued ? ProcessStateAction.Dequeue : ProcessStateAction.Abort,
            PrJobCommand.Cancel when queued => ProcessStateAction.Dequeue,
            _ => null,
        };

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
                Fire(job, job.State == PrJobState.QueuedPooled ? ProcessStateAction.Dequeue : ProcessStateAction.Abort);
            }
        }
    }

    #endregion

    /// <summary>PJ 转了（含刚建好的 #1）：记一行日志，告诉 Job 组件。</summary>
    private void Report(ProcessJob job, int number, PrJobState? from, PrJobState? to)
    {
        LogHelper.Info(LogModule, $"PJ {job.Id} E40 #{number}：{JobNames.Of(from)} → {JobNames.Of(to)}");
        Transitioned?.Invoke(job, number, from, to);
    }
}
