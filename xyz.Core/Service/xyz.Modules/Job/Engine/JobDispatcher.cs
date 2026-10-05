namespace xyz.Modules;

/// <summary>
/// 照调度的计划去提交：起工艺（加工站点自己再查一遍，起不了记成等待）、下搬运单（搬运管理受理时再校验、上锁，不收也记成等待），
/// 再把每片在等什么写上。只在 JobManager 的扫描线程上用。
/// </summary>
internal sealed class JobDispatcher
{
    private readonly JobRuntime _runtime;

    public JobDispatcher(JobRuntime runtime)
    {
        _runtime = runtime;
    }

    public void Apply(JobPlan plan)
    {
        foreach (var process in plan.Processes)
        {
            Start(process);
        }

        foreach (var transfer in plan.Transfers)
        {
            Submit(transfer);
        }

        foreach (var job in _runtime.Book.ProcessJobs)
        {
            foreach (var wafer in job.Wafers)
            {
                if (plan.Waits.TryGetValue(wafer.Id, out var wait))
                {
                    SetWait(wafer, wait);
                }
            }
        }
    }

    private void Start(PlannedProcess planned)
    {
        var station = _runtime.Environment.ProcessStation(planned.Station);
        if (station is null)
        {
            return;
        }

        var job = planned.Job;
        var wafer = planned.Wafer;
        var request = new ProcessRequest
        {
            Origin = ProcessOrigin.Job,
            Owner = job.Id,
            WaferId = wafer.Id,
            Slot = planned.Slot,
            Step = wafer.Step,
            RecipeName = planned.Step.RecipeName,
            Recipe = planned.Step.Recipe,
        };

        var operation = station.StartProcess(request);
        if (operation is null)
        {
            var rejection = station.CheckProcess(request);
            if (rejection is not null)
            {
                SetWait(wafer, new JobWait(rejection.Code, rejection.Args));
            }

            return;
        }

        wafer.Process = operation;
        wafer.Phase = JobWaferPhase.Processing;
        wafer.ProcessStartedAt = DateTime.Now;
        SetWait(wafer, null);
        _runtime.Events.WaferProcessStarted(job, wafer, planned.Station);
        _runtime.Book.Touch();
    }

    private void Submit(PlannedTransfer planned)
    {
        var transfers = _runtime.Environment.Transfers;
        if (transfers is null)
        {
            return;
        }

        var job = planned.Job;
        var wafer = planned.Wafer;
        var ticket = transfers.Submit(new TransferRequest
        {
            Origin = TransferOrigin.Auto,
            Owner = job.Id,
            WaferId = wafer.Id,
            Source = planned.Source,
            SourceSlot = planned.SourceSlot,
            Target = planned.Target,
            TargetSlot = planned.TargetSlot,
            Robot = planned.Robot,
        });

        if (!ticket.Accepted)
        {
            SetWait(wafer, new JobWait(ticket.Code, ticket.Args));
            return;
        }

        wafer.FromStep = wafer.Step;
        wafer.FromPhase = wafer.Phase;
        wafer.Step = planned.TargetStep;
        wafer.Phase = JobWaferPhase.Moving;
        wafer.TransferId = ticket.Id;
        wafer.MovingTo = planned.Target;
        wafer.MovingToSlot = planned.TargetSlot;
        SetWait(wafer, null);
        _runtime.Book.Touch();
    }

    /// <summary>
    /// 改"在等什么"：跟原来一样就不动（每拍都会重算，不能因为同一个原因反复推送）。
    /// </summary>
    public void SetWait(JobWafer wafer, JobWait? wait)
    {
        var current = wafer.Wait;
        bool same = (current is null && wait is null)
            || (current is not null && wait is not null && current.Code == wait.Code && current.Args.SequenceEqual(wait.Args));
        if (same)
        {
            return;
        }

        wafer.Wait = wait;
        _runtime.Book.Touch();
    }
}
