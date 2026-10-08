namespace xyz.Components.Components;

/// <summary>
/// E87 几个小状态机的底子（照老 CTC 的写法）：一张转换表（当前状态 + 消息 → 下一个状态，进状态时做什么），
/// 发消息查表，查到就转、调进状态的方法（带上原来的状态，好按"从哪来"报事件），查不到就不理。
/// 不开线程：调用方都在 E87 的锁里，一条消息转完才回来。
/// </summary>
internal abstract class E87StateMachine<TState, TMessage> where TState : struct, Enum where TMessage : struct, Enum
{
    private readonly Dictionary<(TState, TMessage), (TState Next, Action<TState>? Enter)> _transitions = [];
    private readonly Dictionary<TMessage, (TState Next, Action<TState>? Enter)> _fromAnyState = [];

    protected E87StateMachine(TState initial)
    {
        State = initial;
    }

    /// <summary>当前状态。</summary>
    public TState State { get; private set; }

    /// <summary>
    /// 发一条消息：表里有这一条就转过去，再调进状态的方法；没有就不理（返回 false）。
    /// </summary>
    public bool Post(TMessage message)
    {
        if (!_transitions.TryGetValue((State, message), out var transition) && !_fromAnyState.TryGetValue(message, out transition))
        {
            return false;
        }

        var from = State;
        State = transition.Next;
        transition.Enter?.Invoke(from);
        return true;
    }

    /// <summary>登记一条转换：在 from 收到 message 转到 next，进 next 时调 enter（参数是原来的状态）。</summary>
    protected void Add(TState from, TMessage message, TState next, Action<TState>? enter = null)
    {
        _transitions[(from, message)] = (next, enter);
    }

    /// <summary>登记一条哪个状态都认的转换（比如载具拿走）。</summary>
    protected void AddFromAnyState(TMessage message, TState next, Action<TState>? enter = null)
    {
        _fromAnyState[message] = (next, enter);
    }
}
