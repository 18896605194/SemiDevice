namespace xyz.Modules.StateMachines;

/// <summary>按当前状态和 action 查询状态表并执行转换。</summary>
public abstract class StateMachine<TState, TAction>
    where TState : struct, Enum
    where TAction : struct, Enum
{
    private readonly IReadOnlyDictionary<(TState State, TAction Action), StateTransition<TState>> _transitions;

    protected StateMachine(IReadOnlyDictionary<(TState State, TAction Action), StateTransition<TState>> transitions)
    {
        _transitions = transitions;
    }

    public abstract TState State { get; protected set; }

    public bool Fire(TAction action)
    {
        if (!CanFire(action) || !_transitions.TryGetValue((State, action), out var transition))
        {
            return false;
        }

        var from = State;
        State = transition.State;
        OnTransition(from, transition);
        return true;
    }

    protected virtual bool CanFire(TAction action)
    {
        return true;
    }

    protected abstract void OnTransition(TState from, StateTransition<TState> transition);
}
