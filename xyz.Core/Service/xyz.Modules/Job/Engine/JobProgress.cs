using System.Collections.Concurrent;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 把设备那边的结果落到每一片上：搬运单结束（搬运管理的事件，任意线程进来先排队）、工艺操作收尾完成；
/// 再每拍拿晶圆账核对一遍片在不在该在的地方。只认"做完了"的结果（搬运收尾、记完账才出结果，工艺看 IsSettled），不靠猜。
/// 出执行故障（动过手的搬运失败、加工没做成、片不在该在的地方）就暂停自动派单、保留现场；中止中的 PJ 本来就在收场，不暂停别人。
/// </summary>
internal sealed class JobProgress
{
    private readonly JobRuntime _runtime;
    private readonly ConcurrentQueue<TransferResult> _transfers = new();

    public JobProgress(JobRuntime runtime)
    {
        _runtime = runtime;
    }

    /// <summary>搬运管理的结束事件（任意线程）：先排队，扫描线程里再落。</summary>
    public void Enqueue(TransferResult result)
    {
        _transfers.Enqueue(result);
    }

    /// <summary>落这一拍收到的结果：搬运单、工艺。</summary>
    public void Collect()
    {
        while (_transfers.TryDequeue(out var result))
        {
            Apply(result);
        }

        CollectProcesses();
    }

    private void Apply(TransferResult result)
    {
        string? owner = result.Owner;
        if (owner is null)
        {
            return;
        }

        var job = _runtime.Book.FindProcessJob(owner);
        var wafer = job?.Wafers.FirstOrDefault(item => item.Id == result.WaferId && item.TransferId == result.Id);
        if (job is null || wafer is null)
        {
            return;
        }

        wafer.TransferId = null;
        wafer.MovingTo = null;
        if (result.IsSuccess)
        {
            Arrive(job, wafer, result.Target, result.TargetSlot);
        }
        else if (!result.NeedsRecovery)
        {
            // 没动过手：片还在原处，退回搬运前的样子，下一拍重新排
            wafer.Step = wafer.FromStep;
            wafer.Phase = wafer.FromPhase;
            wafer.Wait = new JobWait(result.Code, result.Args);
        }
        else
        {
            wafer.Phase = JobWaferPhase.Lost;
            job.NeedsRecovery = true;
            if (job.State != PrJobState.Aborting)
            {
                _runtime.Halt(JobWait.Of(ErrorCodes.JobHoldTransferFailed, wafer.Name, job.Id));
            }
        }

        _runtime.Book.Touch();
    }

    /// <summary>到站：回片槽就是这一片结束了；要加工的站点等加工，不加工的站点到了就算这一站做完。</summary>
    private void Arrive(ProcessJob job, JobWafer wafer, string station, int slot)
    {
        wafer.Station = station;
        wafer.Slot = slot;
        if (wafer.Step >= job.Recipe.Steps.Count)
        {
            wafer.Phase = JobWaferPhase.Done;
            return;
        }

        var step = job.Recipe.Steps[wafer.Step];
        wafer.Phase = step.NeedsProcess && _runtime.Environment.IsProcessStation(station)
            ? JobWaferPhase.Arrived
            : JobWaferPhase.Processed;
    }

    /// <summary>工艺操作收尾完成了的（腔体落好状态、账上标好工艺状态）：记这一站的结果；没做成的不再做后面的步骤。</summary>
    private void CollectProcesses()
    {
        foreach (var job in _runtime.Book.ProcessJobs)
        {
            foreach (var wafer in job.Wafers)
            {
                var operation = wafer.Process;
                if (operation is null || !operation.IsSettled)
                {
                    continue;
                }

                wafer.Process = null;
                string station = wafer.Station ?? string.Empty;
                var step = wafer.Step >= 0 && wafer.Step < job.Recipe.Steps.Count ? job.Recipe.Steps[wafer.Step] : null;
                wafer.Results.Add(new JobStepResult
                {
                    Step = wafer.Step,
                    Station = station,
                    Recipe = step?.RecipeName ?? string.Empty,
                    RecipeRevision = step?.Recipe?.Revision ?? 0,
                    Success = operation.IsSuccess,
                    Code = operation.Code,
                    Args = operation.ErrorArgs,
                    Simulated = _runtime.Environment.ProcessStation(station)?.IsProcessSimulated == true,
                    StartedAt = wafer.ProcessStartedAt,
                    EndedAt = DateTime.Now,
                });
                wafer.Phase = JobWaferPhase.Processed;

                if (!operation.IsSuccess)
                {
                    wafer.Failed = true;
                    if (job.State != PrJobState.Aborting)
                    {
                        _runtime.Halt(JobWait.Of(ErrorCodes.JobHoldProcessFailed, wafer.Name, station, job.Id));
                    }
                }

                _runtime.Events.WaferProcessEnded(job, wafer, station, operation.IsSuccess);
                _runtime.Book.Touch();
            }
        }
    }

    /// <summary>
    /// 核对片位：没在途的片（在来源槽等投、在站点上）要正好在账上它该在的位置。不在（被人改了账、载具被拿走、整篮重新 Mapping 过，
    /// 片换了新标识）就认成片位说不准，等人工确认；已经开始的 PJ 出这种事要暂停自动派单，还在排队的只挂恢复标记。
    /// </summary>
    public void RefreshPositions()
    {
        var ledger = _runtime.Environment.Ledger;
        if (ledger is null)
        {
            return;
        }

        foreach (var job in _runtime.Book.ProcessJobs)
        {
            foreach (var wafer in job.Wafers)
            {
                bool waiting = wafer.Phase == JobWaferPhase.Waiting;
                bool parked = wafer.Phase is JobWaferPhase.Arrived or JobWaferPhase.Processing or JobWaferPhase.Processed;
                if (!waiting && !parked)
                {
                    continue;
                }

                string? station = waiting ? wafer.SourcePort : wafer.Station;
                int slot = waiting ? wafer.SourceSlot : wafer.Slot;
                var found = ledger.FindById(wafer.Id);
                if (found is not null && string.Equals(found.Module, station, StringComparison.OrdinalIgnoreCase) && found.Slot == slot)
                {
                    continue;
                }

                wafer.Phase = JobWaferPhase.Lost;
                job.NeedsRecovery = true;
                _runtime.Book.Touch();
                if (job.State != PrJobState.QueuedPooled && job.State != PrJobState.Aborting)
                {
                    _runtime.Halt(JobWait.Of(ErrorCodes.JobHoldWaferLost, wafer.Name, job.Id));
                }
            }
        }
    }
}
