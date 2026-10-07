using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// CJ 管理（SEMI E94）：CJ 队列、历史、E94 状态机（转换表、转、自动往下转）、CJ 命令。
/// CJ 的 Stop / Abort 照 E94 往下传给它的 PJ（经 PJ 管理）。Job 组件建它、管它，只在 Job 组件的锁里用；
/// 转了经 <see cref="Transitioned"/> 告诉 Job 组件，报 EAP、告诉 LoadPort 载具干完了这些牵扯别处的事由 Job 组件做。
/// </summary>
internal sealed class CjManager : ICjManager
{
    /// <summary>E94 #1：建好，进 QUEUED（没有出发状态，建 CJ 时直接报）。</summary>
    public const int CreatedTransition = 1;

    /// <summary>E94 #12：中止做完，进 COMPLETED。</summary>
    public const int AbortedTransition = 12;

    /// <summary>E94 #13：完成后删掉。</summary>
    public const int DeletedTransition = 13;

    private const string LogModule = "Job";

    private readonly IPjManager _processJobs;
    private readonly List<ControlJob> _jobs = [];
    private readonly List<ControlJob> _history = [];
    private readonly List<ControlJobDto> _restored = [];

    public CjManager(IPjManager processJobs)
    {
        _processJobs = processJobs;
    }

    public event Action<ControlJob, int, CtrlJobState?, CtrlJobState?>? Transitioned;

    public IReadOnlyList<ControlJob> Jobs => _jobs;

    public IReadOnlyList<ControlJob> History => _history;

    public IReadOnlyList<ControlJobDto> Restored => _restored;

    public ControlJob? Find(string id)
    {
        return _jobs.FirstOrDefault(job => string.Equals(job.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public ControlJob? On(string loadPort)
    {
        return _jobs.FirstOrDefault(job => string.Equals(job.LoadPort, loadPort, StringComparison.OrdinalIgnoreCase));
    }

    #region 建 CJ

    public HandleResult? TryBuild(ControlJobSpec spec, out ControlJob job, out List<ProcessJob> processes)
    {
        job = null!;
        processes = [];
        string id = spec.Id.Trim();
        string? portName = null;
        foreach (string processId in spec.ProcessJobs)
        {
            var process = _processJobs.Find(processId.Trim());
            if (process is null || process.ControlJob is not null || process.State != PrJobState.QueuedPooled
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

        var busy = On(portName);
        if (busy is not null)
        {
            return HandleResult.Fail(ErrorCodes.JobLoadPortBusy, portName, busy.Id);
        }

        // 载具跟着 PJ：PJ 建的时候 LoadPort 上那个载具
        job = new ControlJob
        {
            Id = id,
            LoadPort = portName,
            CarrierId = processes[0].CarrierId,
            CarrierInstance = processes[0].CarrierInstance,
            LotId = spec.LotId,
            AutoStart = spec.AutoStart,
        };
        return null;
    }

    public void Add(ControlJob job, IReadOnlyList<ProcessJob> processes)
    {
        foreach (var process in processes)
        {
            process.ControlJob = job;
            job.ProcessJobs.Add(process);
        }

        _jobs.Add(job);
        Report(job, CreatedTransition, null, job.State);
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
        if (to == CtrlJobState.Executing && job.StartedAt is null)
        {
            job.StartedAt = now;
        }

        if (to == CtrlJobState.Completed && from != CtrlJobState.Completed)
        {
            job.CompletedAt = now;
            job.CompletedBy = number;
        }

        if (ends)
        {
            job.EndedBy = number;
            job.EndedAt = now;
            _jobs.Remove(job);
            _history.Insert(0, job);
        }

        Report(job, number, from, ends ? null : to);
        return true;
    }

    /// <summary>
    /// SEMI E94 CJ 状态转换表（#2~#13，号码照标准；#1 是建好时报的）：现在的状态收到这个触发能不能转、转到哪。
    /// Ends 为 true 是转完 CJ 就删了，To 是删之前报的最后状态。ACTIVE = 选中、等启动、执行、暂停（标准里的超状态）。
    /// </summary>
    private static (int Number, CtrlJobState To, bool Ends)? Find(ControlJob job, ControlStateAction trigger)
    {
        var state = job.State;
        return trigger switch
        {
            ControlStateAction.Dequeue when state == CtrlJobState.Queued => (2, CtrlJobState.Queued, true),
            ControlStateAction.Select when state == CtrlJobState.Queued => (3, CtrlJobState.Selected, false),
            ControlStateAction.Deselect when state == CtrlJobState.Selected => (4, CtrlJobState.Queued, false),
            ControlStateAction.MaterialReadyStart when state == CtrlJobState.Selected => (5, CtrlJobState.Executing, false),
            ControlStateAction.MaterialReadyWait when state == CtrlJobState.Selected => (6, CtrlJobState.WaitingForStart, false),
            ControlStateAction.Start when state == CtrlJobState.WaitingForStart => (7, CtrlJobState.Executing, false),
            ControlStateAction.Pause when state == CtrlJobState.Executing => (8, CtrlJobState.Paused, false),
            ControlStateAction.Resume when state == CtrlJobState.Paused => (9, CtrlJobState.Executing, false),
            ControlStateAction.AllDone when state == CtrlJobState.Executing => (10, CtrlJobState.Completed, false),
            ControlStateAction.Stopped when job.IsActive => (11, CtrlJobState.Completed, false),
            ControlStateAction.Aborted when job.IsActive => (AbortedTransition, CtrlJobState.Completed, false),
            ControlStateAction.Delete when state == CtrlJobState.Completed => (DeletedTransition, CtrlJobState.Completed, true),
            _ => null,
        };
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
        if (job.State == CtrlJobState.Queued)
        {
            return ControlStateAction.Select;
        }

        // #5 / #6：料到了（LoadPort 上的载具 Load 好、能取片），自动启动的直接执行，手动启动的等 CJStart
        if (job.State == CtrlJobState.Selected && isCarrierReady(job.LoadPort))
        {
            return job.AutoStart ? ControlStateAction.MaterialReadyStart : ControlStateAction.MaterialReadyWait;
        }

        bool allEnded = job.ProcessJobs.All(process => process.IsEnded);

        // #10：没收 Stop / Abort，下面的 PJ 都结束了
        if (job.State == CtrlJobState.Executing && job.Ending == CtrlJobEnding.None && job.ProcessJobs.Count > 0 && allEnded)
        {
            return ControlStateAction.AllDone;
        }

        // #11 / #12：收了 Stop / Abort，下面的 PJ 都结束了（停完、中止完，片位都确定）
        if (job.Ending != CtrlJobEnding.None && job.IsActive && allEnded)
        {
            return job.Ending == CtrlJobEnding.Stop ? ControlStateAction.Stopped : ControlStateAction.Aborted;
        }

        // #13：完成了、载具从 LoadPort 拿走了（或换了一个），转进历史；载具还在时留着给人看结果
        if (job.State == CtrlJobState.Completed && isCarrierGone(job))
        {
            return ControlStateAction.Delete;
        }

        return null;
    }

    #endregion

    #region CJ 命令（E94）

    /// <summary>
    /// CJ 命令（先查 E94 转换表，表里没有的一律拒，回当前状态）。Stop / Abort 照 E94：排队的 PJ 按 Action 留下或删掉，
    /// 在跑的 PJ 各自停止 / 中止，CJ 的状态值不变，等 PJ 都结束再自己转 COMPLETED（#11 / #12）。
    /// </summary>
    public HandleResult Command(string id, CtrlJobCommand command, CtrlJobAction action)
    {
        var job = Find(id.Trim());
        if (job is null || job.IsEnded)
        {
            return HandleResult.Fail(ErrorCodes.JobNotFound, id.Trim());
        }

        string name = JobNames.Of(command);
        if (job.Ending != CtrlJobEnding.None && command is not (CtrlJobCommand.Stop or CtrlJobCommand.Abort))
        {
            return HandleResult.Fail(ErrorCodes.JobEnding, job.Id, name);
        }

        switch (command)
        {
            case CtrlJobCommand.Start:
                return Command(job, ControlStateAction.Start, name);
            case CtrlJobCommand.Pause:
                return Command(job, ControlStateAction.Pause, name);
            case CtrlJobCommand.Resume:
                return Command(job, ControlStateAction.Resume, name);
            case CtrlJobCommand.Deselect:
                return Command(job, ControlStateAction.Deselect, name);
            case CtrlJobCommand.HeadOfQueue:
                return HeadOfQueue(job, name);
            case CtrlJobCommand.Cancel:
                return job.State == CtrlJobState.Queued ? Dequeue(job, action) : NotAllowed(job, name);
            case CtrlJobCommand.Stop:
            case CtrlJobCommand.Abort:
                return End(job, command == CtrlJobCommand.Stop ? CtrlJobEnding.Stop : CtrlJobEnding.Abort, action, name);
            default:
                return NotAllowed(job, name);
        }
    }

    public void AbortAll()
    {
        foreach (var job in _jobs.ToList())
        {
            if (!job.IsEnded && job.State != CtrlJobState.Completed)
            {
                End(job, CtrlJobEnding.Abort, CtrlJobAction.RemoveJobs, JobNames.Of(CtrlJobCommand.Abort));
            }
        }
    }

    /// <summary>
    /// CJStop / CJAbort：排队的 CJ 直接删（#2）；ACTIVE 的收下，排队的 PJ 按 Action 留下或删掉，在跑的 PJ 各自停止 / 中止，
    /// 都结束了再转 COMPLETED（#11 / #12）。停止中又收到中止的升级成中止。
    /// </summary>
    private HandleResult End(ControlJob job, CtrlJobEnding ending, CtrlJobAction action, string name)
    {
        if (job.State == CtrlJobState.Queued)
        {
            return Dequeue(job, action);
        }

        if (job.State == CtrlJobState.Completed)
        {
            return NotAllowed(job, name);
        }

        if (job.Ending == CtrlJobEnding.Abort || job.Ending == ending)
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

            if (process.State == PrJobState.QueuedPooled)
            {
                DropQueued(job, process, action);
                continue;
            }

            // PROCESS COMPLETE 的 PJ 转换表里不收停止 / 中止：片都做完了，回完片自己结束
            _processJobs.Fire(process, ending == CtrlJobEnding.Stop ? ProcessStateAction.Stop : ProcessStateAction.Abort);
        }

        LogHelper.Info(LogModule, $"CJ {job.Id} 收下 {name}（{action}），等下面的 PJ 都结束");
        return HandleResult.Success(job.Id);
    }

    /// <summary>排队的 CJ 删掉（#2）：它的 PJ 按 Action 留下（不再归它）或删掉（#18）。</summary>
    private HandleResult Dequeue(ControlJob job, CtrlJobAction action)
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
    private void DropQueued(ControlJob job, ProcessJob process, CtrlJobAction action)
    {
        if (action == CtrlJobAction.RemoveJobs)
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
        if (job.State != CtrlJobState.Queued)
        {
            return NotAllowed(job, name);
        }

        _jobs.Remove(job);
        int head = _jobs.FindIndex(control => control.State == CtrlJobState.Queued);
        _jobs.Insert(head < 0 ? _jobs.Count : head, job);
        return HandleResult.Success(job.Id);
    }

    private HandleResult Command(ControlJob job, ControlStateAction trigger, string name)
    {
        return Fire(job, trigger) ? HandleResult.Success(job.Id) : NotAllowed(job, name);
    }

    private static HandleResult NotAllowed(ControlJob job, string name)
    {
        return HandleResult.Fail(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
    }

    #endregion

    #region 历史、重启收场

    /// <summary>历史连上次开机留下的一起最多留 keep 个，先扔最老的。</summary>
    public void TrimHistory(int keep)
    {
        int limit = Math.Max(0, keep);
        while (_history.Count + _restored.Count > limit)
        {
            if (_restored.Count > 0)
            {
                _restored.RemoveAt(_restored.Count - 1);
            }
            else
            {
                _history.RemoveAt(_history.Count - 1);
            }
        }
    }

    /// <summary>
    /// 重启收场：上次没结束的 Job 不接着跑——重启前在途的搬运、工艺做没做完说不准，接着派只会把错放大。
    /// 上次没删的 CJ 一律记成中止结束（E94 #12，标着"设备重启"），跟上次的历史一起放进历史；
    /// 机内的片由人确认片位后收回，再重新建 Job（行业里也是这么收场：回片、记异常结束、由 MES / 工程师决定返工还是重做）。
    /// </summary>
    public void CloseOutLastRun(JobListDto? last)
    {
        if (last is null)
        {
            return;
        }

        var now = DateTime.Now;
        var interrupted = new List<ControlJobDto>();
        foreach (var job in last.ControlJobs)
        {
            if (job.State != (int)CtrlJobState.Completed)
            {
                job.State = (int)CtrlJobState.Completed;
                job.CompletedBy = AbortedTransition;
                job.Ending = CtrlJobEnding.Abort.ToString();
                job.CompletedAt = now;
                job.Restarted = true;
                interrupted.Add(job);
            }

            // 完成了还没删的（载具还在）也一样：重启后从队列里拿掉，进历史
            if (job.EndedBy == 0)
            {
                job.EndedBy = DeletedTransition;
                job.EndedAt = now;
            }

            _restored.Add(job);
        }

        _restored.AddRange(last.History);
        if (interrupted.Count > 0)
        {
            string jobs = string.Join("、", interrupted.Select(job => $"{job.Id}（{job.LoadPort}）"));
            LogHelper.Warn(LogModule, $"上次有 {interrupted.Count} 个 Job 没做完就重启了：{jobs}。重启后不接着跑，已记成中止进历史；"
                + "机内的片到现场确认片位后收回，再重新建 Job");
        }
    }

    #endregion

    /// <summary>CJ 转了（含刚建好的 #1）：记一行日志，告诉 Job 组件。</summary>
    private void Report(ControlJob job, int number, CtrlJobState? from, CtrlJobState? to)
    {
        LogHelper.Info(LogModule, $"CJ {job.Id} E94 #{number}：{JobNames.Of(from)} → {JobNames.Of(to)}");
        Transitioned?.Invoke(job, number, from, to);
    }
}
