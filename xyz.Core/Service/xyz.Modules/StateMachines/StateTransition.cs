namespace xyz.Modules.StateMachines;

using xyz.Shared.Dtos;

/// <summary>状态表中的一次转换，定义检查、执行和收尾动作。</summary>
public sealed class StateTransition<TState> where TState : struct, Enum
{
    public TState TargetState { get; set; }
    public TState? ProcessState { get; set; }
    public Action<object[]>? OnEntry { get; set; }
    public Func<object[], bool>? PreCheck { get; set; }
    public Func<object[], HandleResult>? Execute { get; set; }
    public Func<object[], HandleResult>? OnExit { get; set; }
    public Action<HandleResult, object[]>? ErrorHandler { get; set; }
}
