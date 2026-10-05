using System.Globalization;
using xyz.Common.Log;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 执行 Job 命令（只在 JobManager 的扫描线程上跑）：建 Job、CJ / PJ 命令、恢复派单。
/// 能不能做先查 E94 / E40 的转换表（表里没有的一律拒，回当前状态）；CJ 的 Stop / Abort 照 E94：排队的 PJ 按 Action 留下或删掉，
/// 在跑的 PJ 各自停止 / 中止，CJ 的状态值不变，等 PJ 都结束再由规则转 COMPLETED。
/// </summary>
internal sealed class JobCommandHandler
{
    private const string LogModule = "Job";

    private readonly JobRuntime _runtime;
    private readonly JobEngine _engine;
    private readonly JobFactory _factory;

    public JobCommandHandler(JobRuntime runtime, JobEngine engine)
    {
        _runtime = runtime;
        _engine = engine;
        _factory = new JobFactory(runtime);
    }

    #region 建 Job

    public JobCommandResult CreateLocal(LocalJobRequest request)
    {
        var rejected = _factory.TryBuildLocal(request, out var control, out var processes);
        if (rejected is not null)
        {
            return rejected;
        }

        foreach (var process in processes)
        {
            Add(process);
        }

        AddControlJob(control, processes);
        LogHelper.Info(LogModule,
            $"建 Job {control.Id}（{control.LoadPort}，{processes.Count} 个 PJ，{processes.Sum(process => process.Wafers.Count)} 片，操作人 {request.Operator}）");
        return JobCommandResult.Ok(control.Id);
    }

    public JobCommandResult CreateProcessJob(ProcessJobSpec spec, JobCommandSource source)
    {
        var rejected = _factory.TryBuildProcessJob(spec, source, out var process);
        if (rejected is not null)
        {
            return rejected;
        }

        Add(process);
        return JobCommandResult.Ok(process.Id);
    }

    public JobCommandResult CreateControlJob(ControlJobSpec spec, JobCommandSource source)
    {
        var rejected = _factory.TryBuildControlJob(spec, source, out var control, out var processes);
        if (rejected is not null)
        {
            return rejected;
        }

        AddControlJob(control, processes);
        return JobCommandResult.Ok(control.Id);
    }

    /// <summary>PJ 挂进账本：片记到它名下，报 E40 #1。</summary>
    private void Add(ProcessJob process)
    {
        _runtime.Book.ProcessJobs.Add(process);
        _runtime.Book.Own(process);
        _engine.Created(process);
    }

    /// <summary>CJ 挂进账本（排在队尾）：收下它的 PJ，报 E94 #1。</summary>
    private void AddControlJob(ControlJob control, IReadOnlyList<ProcessJob> processes)
    {
        foreach (var process in processes)
        {
            process.ControlJob = control;
            control.ProcessJobs.Add(process);
        }

        _runtime.Book.ControlJobs.Add(control);
        _engine.Created(control);
    }

    #endregion

    #region CJ 命令（E94）

    public JobCommandResult CommandControlJob(string id, CtrlJobCommand command, CtrlJobAction action)
    {
        var job = _runtime.Book.FindControlJob(id.Trim());
        if (job is null || job.IsEnded)
        {
            return JobCommandResult.Reject(ErrorCodes.JobNotFound, id.Trim());
        }

        string name = JobNames.Of(command);
        if (job.Ending != CtrlJobEnding.None && command is not (CtrlJobCommand.Stop or CtrlJobCommand.Abort))
        {
            return JobCommandResult.Reject(ErrorCodes.JobEnding, job.Id, name);
        }

        switch (command)
        {
            case CtrlJobCommand.Start:
                return _runtime.Environment.IsAuto ? Fire(job, CtrlJobTrigger.Start, name) : JobCommandResult.Reject(ErrorCodes.JobNotAuto);

            case CtrlJobCommand.Pause:
                return Fire(job, CtrlJobTrigger.Pause, name);

            case CtrlJobCommand.Resume:
                return Fire(job, CtrlJobTrigger.Resume, name);

            case CtrlJobCommand.Deselect:
                return Fire(job, CtrlJobTrigger.Deselect, name);

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

    /// <summary>
    /// CJStop / CJAbort：排队的 CJ 直接删（#2）；ACTIVE 的收下，排队的 PJ 按 Action 留下或删掉，在跑的 PJ 各自停止 / 中止，
    /// 都结束了规则再转 COMPLETED（#11 / #12）。停止中又收到中止的升级成中止。
    /// </summary>
    private JobCommandResult End(ControlJob job, CtrlJobEnding ending, CtrlJobAction action, string name)
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
            return JobCommandResult.Ok(job.Id);
        }

        job.Ending = ending;
        _runtime.Book.Touch();
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
            _engine.Fire(process, ending == CtrlJobEnding.Stop ? PrJobTrigger.Stop : PrJobTrigger.Abort);
        }

        LogHelper.Info(LogModule, $"CJ {job.Id} 收下 {name}（{action}），等下面的 PJ 都结束");
        return JobCommandResult.Ok(job.Id);
    }

    /// <summary>排队的 CJ 删掉（#2）：它的 PJ 按 Action 留下（不再归它）或删掉（#18）。</summary>
    private JobCommandResult Dequeue(ControlJob job, CtrlJobAction action)
    {
        foreach (var process in job.ProcessJobs.ToList())
        {
            if (!process.IsEnded)
            {
                DropQueued(job, process, action);
            }
        }

        _engine.Fire(job, CtrlJobTrigger.Dequeue);
        return JobCommandResult.Ok(job.Id);
    }

    /// <summary>排队的 PJ：SaveJobs 留下（脱离这个 CJ，等别的 CJ 收）；RemoveJobs 删掉（#18）。</summary>
    private void DropQueued(ControlJob job, ProcessJob process, CtrlJobAction action)
    {
        if (action == CtrlJobAction.RemoveJobs)
        {
            _engine.Fire(process, PrJobTrigger.Dequeue);
            return;
        }

        process.ControlJob = null;
        job.ProcessJobs.Remove(process);
        _runtime.Book.Touch();
    }

    /// <summary>CJHOQ：排队的 CJ 插到排队的最前面。</summary>
    private JobCommandResult HeadOfQueue(ControlJob job, string name)
    {
        if (job.State != CtrlJobState.Queued)
        {
            return NotAllowed(job, name);
        }

        var queue = _runtime.Book.ControlJobs;
        queue.Remove(job);
        int head = queue.FindIndex(control => control.State == CtrlJobState.Queued);
        queue.Insert(head < 0 ? queue.Count : head, job);
        _runtime.Book.Touch();
        return JobCommandResult.Ok(job.Id);
    }

    private JobCommandResult Fire(ControlJob job, CtrlJobTrigger trigger, string name)
    {
        return _engine.Fire(job, trigger) ? JobCommandResult.Ok(job.Id) : NotAllowed(job, name);
    }

    private static JobCommandResult NotAllowed(ControlJob job, string name)
    {
        return JobCommandResult.Reject(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
    }

    #endregion

    #region PJ 命令（E40）

    public JobCommandResult CommandProcessJob(string id, PrJobCommand command)
    {
        var job = _runtime.Book.FindProcessJob(id.Trim());
        if (job is null || job.IsEnded)
        {
            return JobCommandResult.Reject(ErrorCodes.JobNotFound, id.Trim());
        }

        string name = JobNames.Of(command);
        bool queued = job.State == PrJobState.QueuedPooled;
        return command switch
        {
            PrJobCommand.Start => _runtime.Environment.IsAuto ? Fire(job, PrJobTrigger.Start, name) : JobCommandResult.Reject(ErrorCodes.JobNotAuto),
            PrJobCommand.Pause => Fire(job, PrJobTrigger.Pause, name),
            PrJobCommand.Resume => Fire(job, PrJobTrigger.Resume, name),
            PrJobCommand.Stop => Fire(job, queued ? PrJobTrigger.Dequeue : PrJobTrigger.Stop, name),
            PrJobCommand.Abort => Fire(job, queued ? PrJobTrigger.Dequeue : PrJobTrigger.Abort, name),
            PrJobCommand.Cancel => queued ? Fire(job, PrJobTrigger.Dequeue, name) : NotAllowed(job, name),
            _ => NotAllowed(job, name),
        };
    }

    private JobCommandResult Fire(ProcessJob job, PrJobTrigger trigger, string name)
    {
        return _engine.Fire(job, trigger) ? JobCommandResult.Ok(job.Id) : NotAllowed(job, name);
    }

    private static JobCommandResult NotAllowed(ProcessJob job, string name)
    {
        return JobCommandResult.Reject(ErrorCodes.JobCommandNotAllowed, job.Id, name, JobNames.Of(job.State));
    }

    #endregion

    #region 整机停止、恢复派单

    /// <summary>
    /// 整机 Stop：所有没结束的 CJ 都走中止（排队的 PJ 一起删），不归 CJ 的 PJ 也中止。中止的过程照常等设备确认、核对片位。
    /// </summary>
    public JobCommandResult AbortAll()
    {
        foreach (var job in _runtime.Book.ControlJobs.ToList())
        {
            if (!job.IsEnded && job.State != CtrlJobState.Completed)
            {
                End(job, CtrlJobEnding.Abort, CtrlJobAction.RemoveJobs, JobNames.Of(CtrlJobCommand.Abort));
            }
        }

        foreach (var process in _runtime.Book.ProcessJobs.ToList())
        {
            if (!process.IsEnded && process.ControlJob is null)
            {
                _engine.Fire(process, process.State == PrJobState.QueuedPooled ? PrJobTrigger.Dequeue : PrJobTrigger.Abort);
            }
        }

        return JobCommandResult.Ok(string.Empty);
    }

    /// <summary>
    /// 恢复派单：人到现场确认过片位、在账单调整页对好了账再调。出错的搬运单都放了锁才收；
    /// 片位说不准的片按账上现在的位置认回来——在来源槽里没动过的回到待投，在别的槽里的不再做后面的步骤、直接回片，
    /// 账上找不到的算被人拿走了；还在机械手手上的认不回来（先放到站点上再恢复）。
    /// </summary>
    public JobCommandResult Recover()
    {
        bool needs = _runtime.Hold is not null || _runtime.Book.ProcessJobs.Any(job => job.NeedsRecovery);
        if (!needs)
        {
            return JobCommandResult.Reject(ErrorCodes.JobNotHeld);
        }

        int held = _runtime.Environment.Transfers?.HeldResults.Count ?? 0;
        if (held > 0)
        {
            return JobCommandResult.Reject(ErrorCodes.JobRecoveryPending, held.ToString(CultureInfo.InvariantCulture));
        }

        int unresolved = 0;
        foreach (var job in _runtime.Book.ProcessJobs)
        {
            foreach (var wafer in job.Wafers)
            {
                if (wafer.Phase == JobWaferPhase.Lost && !Resync(job, wafer))
                {
                    unresolved++;
                }
            }

            job.NeedsRecovery = job.Wafers.Any(wafer => wafer.Phase == JobWaferPhase.Lost);
        }

        _runtime.Book.Touch();
        if (unresolved > 0)
        {
            return JobCommandResult.Reject(ErrorCodes.JobRecoveryPending, unresolved.ToString(CultureInfo.InvariantCulture));
        }

        _runtime.Release();
        return JobCommandResult.Ok(string.Empty);
    }

    /// <summary>按账上现在的位置把一片认回来；认不回来（在机械手上）返回 false。</summary>
    private bool Resync(ProcessJob job, JobWafer wafer)
    {
        var environment = _runtime.Environment;
        var found = environment.Ledger?.FindById(wafer.Id);
        wafer.TransferId = null;
        wafer.Process = null;

        // 该做的加工都做成了没有：没做全的，认回来以后按失败算（不再接着做）
        int needed = job.Recipe.Steps.Count(step => step.NeedsProcess);
        bool complete = !wafer.Failed && wafer.Results.Count(result => result.Success) >= needed;
        if (found is null)
        {
            // 账上没有了：人工拿走了，这片在这个 Job 里到此为止
            wafer.Phase = JobWaferPhase.Done;
            wafer.Outcome = JobWaferOutcome.Aborted;
            return true;
        }

        bool atSource = string.Equals(found.Module, wafer.SourcePort, StringComparison.OrdinalIgnoreCase) && found.Slot == wafer.SourceSlot;
        bool atReturn = string.Equals(found.Module, wafer.ReturnPort, StringComparison.OrdinalIgnoreCase) && found.Slot == wafer.ReturnSlot;
        if (atSource && wafer.Results.Count == 0 && wafer.Step < 0)
        {
            wafer.Phase = JobWaferPhase.Waiting;
            wafer.Station = found.Module;
            wafer.Slot = found.Slot;
            return true;
        }

        if (atReturn)
        {
            wafer.Phase = JobWaferPhase.Done;
            wafer.Station = found.Module;
            wafer.Slot = found.Slot;
            wafer.Failed = !complete;
            return true;
        }

        if (environment.Station(found.Module) is null)
        {
            // 在机械手手上这类不是站点的地方：搬运没法从这儿取，先人工放到站点上再恢复
            return false;
        }

        // 在别的站点上：不再做后面的步骤，直接回片（加工都做成了的照常算完成）
        wafer.Phase = JobWaferPhase.Processed;
        wafer.Failed = !complete;
        wafer.Step = job.Recipe.Steps.Count - 1;
        wafer.Station = found.Module;
        wafer.Slot = found.Slot;
        return true;
    }

    #endregion
}
