namespace xyz.Modules.StateMachines;

/// <summary>状态表中的一次转换。</summary>
public sealed class StateTransition<TState> where TState : struct, Enum
{
    public TState State { get; }

    /// <summary>用于 EAP 上报的转换编号。</summary>
    public int Number { get; }

    /// <summary>转换后是否移除运行对象。</summary>
    public bool Ends { get; }

    public StateTransition(TState state, int number, bool ends = false)
    {
        State = state;
        Number = number;
        Ends = ends;
    }
}
