using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

internal sealed class CjManager : ICjManager
{
    private const string LogModule = "Job";

    private readonly List<ControlJob> _jobs = [];
    public IReadOnlyList<ControlJob> Jobs => _jobs;

    private readonly IPjManager _processJobs;
    public CjManager(IPjManager processJobs)
    {
        _processJobs = processJobs;
    }

    public event Action<ControlJob, int>? StateChanged;

    

    public ControlJob? Find(string id)
    {
        return _jobs.FirstOrDefault(job => string.Equals(job.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public ControlJob? FindByLoadPort(string loadPort)
    {
        return _jobs.FirstOrDefault(job => string.Equals(job.LoadPort, loadPort, StringComparison.OrdinalIgnoreCase));
    }

    #region 建 CJ

    public HandleResult Create(ControlJobSpec spec)
    {
        var processes = new List<ProcessJob>();
        string id = spec.Id.Trim();
        string? portName = null;
        foreach (string processId in spec.ProcessJobs)
        {
            var process = _processJobs.Find(processId.Trim());
            if (process is null || process.ControlJob is not null || process.State != ProcessJobState.QueuedPooled
                || processes.Contains(process))
            {
                return HandleResult.Fail(ErrorCodes.JobProcessJobUnavailable, processId.Trim());
            }

            string port = process.Rows.Count > 0 ? process.Rows[0].SourcePort : string.Empty;
            if (portName is not null && !string.Equals(portName, port, StringComparison.OrdinalIgnoreCase))
            {
                return HandleResult.Fail(ErrorCodes.JobProcessJobUnavailable, processId.Trim());
            }

            portName = port;
            processes.Add(process);
        }

        if (processes.Count == 0 || portName is null)
        {
            return HandleResult.Fail(ErrorCodes.JobNoWafers, id);
        }

        var busy = FindByLoadPort(portName);
        if (busy is not null)
        {
            return HandleResult.Fail(ErrorCodes.JobLoadPortBusy, portName, busy.Id);
        }

        // 载具跟着 PJ：PJ 建的时候 LoadPort 上那个载具
        var job = new ControlJob
        {
            Id = id,
            LoadPort = portName,
            CarrierId = processes[0].CarrierId,
            CarrierInstance = processes[0].CarrierInstance,
            LotId = spec.LotId,
            AutoStart = spec.AutoStart,
        };

        // 收下它的 PJ，排到队尾
        foreach (var process in processes)
        {
            process.ControlJob = job;
            job.ProcessJobs.Add(process);
        }

        _jobs.Add(job);

        // E94 #1：建好进 QUEUED，没有出发状态
        Report(job, 1, null, job.State);
        return HandleResult.Success(job.Id);
    }

    #endregion

    #region E94 状态机

    /// <summary>
    /// 转一次：E94 转换表里有才转（<see cref="Find(ControlJob, ControlStateAction)"/>）。进 EXECUTING 记开始时刻；进 COMPLETED 记完成走的转换号；
    /// 删掉了从队列挪进历史。最后告诉 Job 组件。
    /// </summary>
    private bool Fire(ControlJob job, ControlStateAction trigger)
    {
        var found = job.IsEnded ? null : Find(job, trigger);
        if (found is null)
        {
            return false;
        }

        var (number, to, ends) = found.Value;
        var from = job.State;
        job.State = to;

        var now = DateTime.Now;
        if (to == ControlJobState.Executing && job.StartedAt is null)
        {
            job.StartedAt = now;
        }

        if (to == ControlJobState.Completed && from != ControlJobState.Completed)
        {
            job.CompletedAt = now;
            job.CompletedBy = number;
        }

        if (ends)
        {
            job.EndedBy = number;
            job.EndedAt = now;
            _jobs.Remove(job);
        }

        Report(job, number, from, ends ? null : to);
        return true;
    }

    /// <summary>
    /// SEMI E94 CJ 状态转换表（#2~#13，号码照标准；#1 是建好时报的）：现在的状态收到这个触发能不能转、转到哪。
    /// Ends 为 true 是转完 CJ 就删了，To 是删之前报的最后状态。ACTIVE = 选中、等启动、执行、暂停（标准里的超状态）。
    /// </summary>
    private static (int Number, ControlJobState To, bool Ends)? Find(ControlJob job, ControlStateAction trigger)
    {
        var state = job.State;
        switch (trigger)
        {
            case ControlStateAction.Dequeue:
                if (state == ControlJobState.Queued)
                {
                    return (2, ControlJobState.Queued, true);
                }

                break;

            case ControlStateAction.Select:
                if (state == ControlJobState.Queued)
                {
                    return (3, ControlJobState.Selected, false);
                }

                break;

            case ControlStateAction.Deselect:
                if (state == ControlJobState.Selected)
                {
                    return (4, ControlJobState.Queued, false);
                }

                break;

            case ControlStateAction.MaterialReadyStart:
                if (state == ControlJobState.Selected)
                {
                    return (5, ControlJobState.Executing, false);
                }

                break;

            case ControlStateAction.MaterialReadyWait:
                if (state == ControlJobState.Selected)
                {
                    return (6, ControlJobState.WaitingForStart, false);
                }

                break;

            case ControlStateAction.Start:
                if (state == ControlJobState.WaitingForStart)
                {
                    return (7, ControlJobState.Executing, false);
                }

                break;

            case ControlStateAction.Pause:
                if (state == ControlJobState.Executing)
                {
                    return (8, ControlJobState.Paused, false);
                }

                break;

            case ControlStateAction.Resume:
                if (state == ControlJobState.Paused)
                {
                    return (9, ControlJobState.Executing, false);
                }

                break;

            case ControlStateAction.AllDone:
                if (state == ControlJobState.Executing)
                {
                    return (10, ControlJobState.Completed, false);
                }

                break;

            case ControlStateAction.Stopped:
                if (job.IsActive)
                {
                    return (11, ControlJobState.Completed, false);
                }

                break;

            case ControlStateAction.Aborted:
                if (job.IsActive)
                {
                    return (12, ControlJobState.Completed, false);
                }

                break;

            case ControlStateAction.Delete:
                if (state == ControlJobState.Completed)
                {
                    return (13, ControlJobState.Completed, true);
                }

                break;
        }

        return null;
    }

    public bool Advance(Func<string, bool> isCarrierReady, Func<ControlJob, bool> isCarrierGone)
    {
        bool changed = false;
        foreach (var job in _jobs.ToList())
        {
            var trigger = NextTrigger(job, isCarrierReady, isCarrierGone);
            if (trigger is not null && Fire(job, trigger.Value))
            {
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>自动转换：这个 CJ 现在该不该自己往下转、转哪条（不该转返回 null）。载具在不在、好没好由 Job 组件查设备。</summary>
    private static ControlStateAction? NextTrigger(ControlJob job, Func<string, bool> isCarrierReady, Func<ControlJob, bool> isCarrierGone)
    {
        // #3：排队的直接选中（按队列先后）。不限同时跑几个：一个 LoadPort 上只有一个 CJ，个数自然有数
        if (job.State == ControlJobState.Queued)
        {
            return ControlStateAction.Select;
        }

        // #5 / #6：料到了（LoadPort 上的载具 Load 好、能取片），自动启动的直接执行，手动启动的等 CJStart
        if (job.State == ControlJobState.Selected && isCarrierReady(job.LoadPort))
        {
            return job.AutoStart ? ControlStateAction.MaterialReadyStart : ControlStateAction.MaterialReadyWait;
        }

        bool allEnded = job.ProcessJobs.All(process => process.IsEnded);

        // #10：没收 Stop / Abort，下面的 PJ 都结束了
        if (job.State == ControlJobState.Executing && job.Ending == ControlJobEnding.None && job.ProcessJobs.Count > 0 && allEnded)
        {
            return ControlStateAction.AllDone;
        }

        // #11 / #12：收了 Stop / Abort，下面的 PJ 都结束了（停完、中止完，片位都确定）
        if (job.Ending != ControlJobEnding.None && job.IsActive && allEnded)
        {
            return job.Ending == ControlJobEnding.Stop ? ControlStateAction.Stopped : ControlStateAction.Aborted;
        }

        // #13：完成了、载具从 LoadPort 拿走了（或换了一个），转进历史；载具还在时留着给人看结果
        if (job.State == ControlJobState.Completed && isCarrierGone(job))
        {
            return ControlStateAction.Delete;
        }

        return null;
    }

    #endregion

    #region CJ 命令（E94）

    /// <summary>
    /// 执行 CJ 命令（先查 E94 转换表，表里没有的一律拒，回当前状态）。Stop / Abort 照 E94：排队的 PJ 按 Action 留下或删掉，
    /// 在跑的 PJ 各自停止 / 中止，CJ 的状态值不变，等 PJ 都结束再自己转 COMPLETED（#11 / #12）。
    /// </summary>
    public HandleResult Execute(string id, ControlJobCommand command, ControlJobAction action)
    {
        var job = Find(id.Trim());
        if (job is null || job.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id.Trim());
        }

        string name = JobNames.Of(command);
        if (job.Ending != ControlJobEnding.None && command != ControlJobCommand.Stop && command != ControlJobCommand.Abort)
        {
            return HandleResult.Fail(ErrorCodes.JobEnding, job.Id, name);
        }

        switch (command)
        {
            case ControlJobCommand.Start:
                return Fire(job, ControlStateAction.Start) ? HandleResult.Success(job.Id) : NotAllowed(job, name);
            case ControlJobCommand.Pause:
                return Fire(job, ControlStateAction.Pause) ? HandleResult.Success(job.Id) : NotAllowed(job, name);
            case ControlJobCommand.Resume:
                return Fire(job, ControlStateAction.Resume) ? HandleResult.Success(job.Id) : NotAllowed(job, name);
            case ControlJobCommand.Deselect:
                return Fire(job, ControlStateAction.Deselect) ? HandleResult.Success(job.Id) : NotAllowed(job, name);
            case ControlJobCommand.HeadOfQueue:
                return HeadOfQueue(job, name);
            case ControlJobCommand.Cancel:
                return job.State == ControlJobState.Queued ? Dequeue(job, action) : NotAllowed(job, name);
            case ControlJobCommand.Stop:
            case ControlJobCommand.Abort:
                return End(job, command == ControlJobCommand.Stop ? ControlJobEnding.Stop : ControlJobEnding.Abort, action, name);
            default:
                return NotAllowed(job, name);
        }
    }

    public void AbortAll()
    {
        foreach (var job in _jobs.ToList())
        {
            if (!job.IsEnded && job.State != ControlJobState.Completed)
            {
                End(job, ControlJobEnding.Abort, ControlJobAction.RemoveJobs, JobNames.Of(ControlJobCommand.Abort));
            }
        }
    }

    /// <summary>
    /// CJStop / CJAbort：排队的 CJ 直接删（#2）；ACTIVE 的收下，排队的 PJ 按 Action 留下或删掉，在跑的 PJ 各自停止 / 中止，
    /// 都结束了再转 COMPLETED（#11 / #12）。停止中又收到中止的升级成中止。
    /// </summary>
    private HandleResult End(ControlJob job, ControlJobEnding ending, ControlJobAction action, string name)
    {
        if (job.State == ControlJobState.Queued)
        {
            return Dequeue(job, action);
        }

        if (job.State == ControlJobState.Completed)
        {
            return NotAllowed(job, name);
        }

        if (job.Ending == ControlJobEnding.Abort || job.Ending == ending)
        {
            return HandleResult.Success(job.Id);
        }

        job.Ending = ending;
        foreach (var process in job.ProcessJobs.ToList())
        {
            if (process.IsEnded)
            {
                continue;
            }

            if (process.State == ProcessJobState.QueuedPooled)
            {
                DropQueued(job, process, action);
                continue;
            }

            // PROCESS COMPLETE 的 PJ 转换表里不收停止 / 中止：片都做完了，回完片自己结束
            _processJobs.Fire(process, ending == ControlJobEnding.Stop ? ProcessStateAction.Stop : ProcessStateAction.Abort);
        }

        LogHelper.Info(LogModule, $"CJ {job.Id} 收下 {name}（{action}），等下面的 PJ 都结束");
        return HandleResult.Success(job.Id);
    }

    /// <summary>排队的 CJ 删掉（#2）：它的 PJ 按 Action 留下（不再归它）或删掉（#18）。</summary>
    private HandleResult Dequeue(ControlJob job, ControlJobAction action)
    {
        foreach (var process in job.ProcessJobs.ToList())
        {
            if (!process.IsEnded)
            {
                DropQueued(job, process, action);
            }
        }

        Fire(job, ControlStateAction.Dequeue);
        return HandleResult.Success(job.Id);
    }

    /// <summary>排队的 PJ：SaveJobs 留下（脱离这个 CJ，等别的 CJ 收）；RemoveJobs 删掉（#18）。</summary>
    private void DropQueued(ControlJob job, ProcessJob process, ControlJobAction action)
    {
        if (action == ControlJobAction.RemoveJobs)
        {
            _processJobs.Fire(process, ProcessStateAction.Dequeue);
            return;
        }

        process.ControlJob = null;
        job.ProcessJobs.Remove(process);
    }

    /// <summary>CJHOQ：排队的 CJ 插到排队的最前面。</summary>
    private HandleResult HeadOfQueue(ControlJob job, string name)
    {
        if (job.State != ControlJobState.Queued)
        {
            return NotAllowed(job, name);
        }

        _jobs.Remove(job);
        int head = _jobs.FindIndex(control => control.State == ControlJobState.Queued);
        _jobs.Insert(head < 0 ? _jobs.Count : head, job);
        return HandleResult.Success(job.Id);
    }

    private static HandleResult NotAllowed(ControlJob job, string name)
    {
        return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
    }

    #endregion

    /// <summary>CJ 转了（含刚建好的 #1）：记一行日志，告诉 Job 组件。</summary>
    private void Report(ControlJob job, int number, ControlJobState? from, ControlJobState? to)
    {
        LogHelper.Info(LogModule, $"CJ {job.Id} E94 #{number}：{JobNames.Of(from)} → {JobNames.Of(to)}");
        StateChanged?.Invoke(job, number);
    }
}
