using xyz.Components.Enums;
using xyz.Modules.StateMachines;

namespace xyz.Modules;

/// <summary>一个 CJ 的状态机；状态直接保存在对应的 ControlJob 中。</summary>
public sealed class CjStateMachine : StateMachine<ControlJobState, ControlStateAction>
{
    private readonly ControlJob _job;

    public CjStateMachine(ControlJob job) : base(ControlJobStateTable.Transitions)
    {
        _job = job;
    }

    public override ControlJobState State
    {
        get { return _job.State; }
        protected set { _job.State = value; }
    }

    internal event Action<ControlJob, ControlJobState, ControlJobState>? StateChanged;

    #region CJ 状态转换

    protected override bool CanFire(ControlStateAction action)
    {
        return !_job.IsEnded;
    }

    protected override void OnTransition(ControlJobState from, StateTransition<ControlJobState> transition)
    {
        var job = _job;
        var to = transition.State;
        int number = transition.Number;
        job.TransitionNumber = number;

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

        if (transition.Ends)
        {
            job.EndedBy = number;
            job.EndedAt = now;
        }

        StateChanged?.Invoke(job, from, to);
    }

    #endregion
}
