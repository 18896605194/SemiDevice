namespace xyz.Modules;

/// <summary>
/// PJ 转换之后要做的事（状态已经落好）：一种事一个效果类，引擎每次转换挨个问一遍，跟这次转换没关系的直接返回。
/// </summary>
internal interface IProcessJobEffect
{
    void Apply(ProcessJob job, JobTransition<PrJobState> transition, PrJobState from, JobRuntime runtime);
}

/// <summary>
/// CJ 转换之后要做的事（状态已经落好）。
/// </summary>
internal interface IControlJobEffect
{
    void Apply(ControlJob job, JobTransition<CtrlJobState> transition, CtrlJobState from, JobRuntime runtime);
}

/// <summary>
/// 进 ABORTING（E40 #13 / #14 / #15）：撤这个 PJ 还没开始的搬运单、中止它在跑的搬运（手臂在动的由搬运管理发设备中止），
/// 再给正在给它做工艺的腔体发中止。中止做完、片位确定，规则才会把 PJ 转成结束（#16）。
/// </summary>
internal sealed class AbortProcessJobEffect : IProcessJobEffect
{
    public void Apply(ProcessJob job, JobTransition<PrJobState> transition, PrJobState from, JobRuntime runtime)
    {
        if (job.State != PrJobState.Aborting || from == PrJobState.Aborting)
        {
            return;
        }

        runtime.Environment.Transfers?.CancelOwner(job.Id, $"PJ {job.Id} 中止");

        foreach (var wafer in job.Wafers)
        {
            string? station = wafer.Station;
            if (wafer.Process is null || station is null)
            {
                continue;
            }

            var module = runtime.Environment.Module(station);
            if (module is IProcessStation process && string.Equals(process.CurrentProcess?.Owner, job.Id, StringComparison.OrdinalIgnoreCase))
            {
                module.Abort();
            }
        }
    }
}

/// <summary>
/// PJ 结束（E40 #7 / #16 / #17 / #18）：每片定下最终结果（回片了的按加工成没成，没投的记未执行，还在机内的记中止），
/// 放开它名下的片，从没结束的 PJ 里拿掉（还留在所属 CJ 的列表里给界面看）。
/// </summary>
internal sealed class EndProcessJobEffect : IProcessJobEffect
{
    public void Apply(ProcessJob job, JobTransition<PrJobState> transition, PrJobState from, JobRuntime runtime)
    {
        if (!transition.Ends)
        {
            return;
        }

        foreach (var wafer in job.Wafers)
        {
            wafer.Wait = null;
            if (wafer.Outcome != JobWaferOutcome.None)
            {
                continue;
            }

            wafer.Outcome = wafer.Phase switch
            {
                JobWaferPhase.Done => wafer.Failed ? JobWaferOutcome.Failed : JobWaferOutcome.Completed,
                JobWaferPhase.Waiting => JobWaferOutcome.NotRun,
                _ => JobWaferOutcome.Aborted,
            };
        }

        runtime.Book.Release(job);
        runtime.Book.ProcessJobs.Remove(job);
    }
}

/// <summary>
/// CJ 进 COMPLETED（E94 #10 / #11 / #12）：告诉 LoadPort 这个载具的活干完了（转成 E87 的 CarrierComplete 上报）。
/// </summary>
internal sealed class CarrierCompleteEffect : IControlJobEffect
{
    public void Apply(ControlJob job, JobTransition<CtrlJobState> transition, CtrlJobState from, JobRuntime runtime)
    {
        if (job.State != CtrlJobState.Completed || from == CtrlJobState.Completed)
        {
            return;
        }

        runtime.Environment.LoadPort(job.LoadPort)?.NoteCarrierComplete();
    }
}

/// <summary>
/// CJ 删掉（E94 #2 / #13）：从队列挪进历史。
/// </summary>
internal sealed class ArchiveControlJobEffect : IControlJobEffect
{
    public void Apply(ControlJob job, JobTransition<CtrlJobState> transition, CtrlJobState from, JobRuntime runtime)
    {
        if (!transition.Ends)
        {
            return;
        }

        runtime.Book.Archive(job, runtime.Limits.HistoryKeep());
    }
}
