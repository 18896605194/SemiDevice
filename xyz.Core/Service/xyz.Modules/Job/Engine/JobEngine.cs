namespace xyz.Modules;

/// <summary>
/// 状态转换引擎：查 E40 / E94 转换表，能转就落状态（记时刻、结束标记），再挨个跑效果、最后报事件。
/// 命令和规则都只通过它改 PJ、CJ 的状态，表里没有的转换一律不做。只在 JobManager 的扫描线程上用。
/// </summary>
internal sealed class JobEngine
{
    private readonly JobRuntime _runtime;
    private readonly IReadOnlyList<IProcessJobEffect> _processEffects;
    private readonly IReadOnlyList<IControlJobEffect> _controlEffects;

    public JobEngine(JobRuntime runtime, IReadOnlyList<IProcessJobEffect> processEffects, IReadOnlyList<IControlJobEffect> controlEffects)
    {
        _runtime = runtime;
        _processEffects = processEffects;
        _controlEffects = controlEffects;
    }

    /// <summary>这个 PJ 现在收不收这个触发。</summary>
    public static bool CanFire(ProcessJob job, PrJobTrigger trigger)
    {
        return !job.IsEnded && E40Transitions.Table.Allows(job.State, trigger);
    }

    /// <summary>这个 CJ 现在收不收这个触发。</summary>
    public static bool CanFire(ControlJob job, CtrlJobTrigger trigger)
    {
        return !job.IsEnded && E94Transitions.Table.Allows(job.State, trigger);
    }

    /// <summary>
    /// PJ 转换：表里有才转。暂停（#8）记下暂停前的执行子状态，恢复（#10）回到那里。转成了返回 true。
    /// </summary>
    public bool Fire(ProcessJob job, PrJobTrigger trigger)
    {
        if (job.IsEnded || !E40Transitions.Table.TryGet(job.State, trigger, out var transition))
        {
            return false;
        }

        var from = job.State;
        if (trigger == PrJobTrigger.Pause)
        {
            job.ResumeState = from;
        }

        var to = transition.ToPrevious ? job.ResumeState : transition.To;
        job.State = to;

        var now = DateTime.Now;
        if (to == PrJobState.Processing && job.StartedAt is null)
        {
            job.StartedAt = now;
        }

        if (transition.Ends)
        {
            job.EndedBy = transition.Number;
            job.EndedAt = now;
        }

        foreach (var effect in _processEffects)
        {
            effect.Apply(job, transition, from, _runtime);
        }

        _runtime.Events.ProcessJobTransitioned(job, transition.Number, from, transition.Ends ? null : to);
        _runtime.Book.Touch();
        return true;
    }

    /// <summary>
    /// CJ 转换：表里有才转。进 EXECUTING 记开始时刻，进 COMPLETED 记完成走的转换号，删掉记删掉的转换号。
    /// </summary>
    public bool Fire(ControlJob job, CtrlJobTrigger trigger)
    {
        if (job.IsEnded || !E94Transitions.Table.TryGet(job.State, trigger, out var transition))
        {
            return false;
        }

        var from = job.State;
        var to = transition.To;
        job.State = to;

        var now = DateTime.Now;
        if (to == CtrlJobState.Executing && job.StartedAt is null)
        {
            job.StartedAt = now;
        }

        if (to == CtrlJobState.Completed && from != CtrlJobState.Completed)
        {
            job.CompletedAt = now;
            job.CompletedBy = transition.Number;
        }

        if (transition.Ends)
        {
            job.EndedBy = transition.Number;
            job.EndedAt = now;
        }

        foreach (var effect in _controlEffects)
        {
            effect.Apply(job, transition, from, _runtime);
        }

        _runtime.Events.ControlJobTransitioned(job, transition.Number, from, transition.Ends ? null : to);
        _runtime.Book.Touch();
        return true;
    }

    /// <summary>PJ 建好（E40 #1）。</summary>
    public void Created(ProcessJob job)
    {
        _runtime.Events.ProcessJobTransitioned(job, E40Transitions.Created, null, job.State);
        _runtime.Book.Touch();
    }

    /// <summary>CJ 建好（E94 #1）。</summary>
    public void Created(ControlJob job)
    {
        _runtime.Events.ControlJobTransitioned(job, E94Transitions.Created, null, job.State);
        _runtime.Book.Touch();
    }
}
