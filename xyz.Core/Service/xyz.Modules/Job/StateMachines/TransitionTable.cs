namespace xyz.Modules;

/// <summary>
/// 查表查到的一条转换：标准里的转换号、去哪个状态。
/// <see cref="Ends"/> 为 true 时转完这个 Job 就结束了（标准里的 no state），<see cref="To"/> 是结束前报的最后状态值；
/// <see cref="ToPrevious"/> 为 true 时回到之前记下的状态（E40 #10：恢复回暂停前的执行子状态），<see cref="To"/> 不用。
/// </summary>
public readonly record struct JobTransition<TState>(int Number, TState To, bool Ends, bool ToPrevious)
    where TState : struct, Enum;

/// <summary>
/// 表驱动状态机：(当前状态, 触发) → 转换。SEMI 标准规定的转换写成表，代码里不散写"什么状态能干什么"的判断；
/// 命令收不收、自动往下转不转，都查这张表。同一个(状态, 触发)写两遍是配错了，开机就抛。
/// </summary>
public sealed class TransitionTable<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private readonly Dictionary<(TState State, TTrigger Trigger), JobTransition<TState>> _rows = new();

    /// <summary>普通转换：from 收到 trigger 去 to。</summary>
    public TransitionTable<TState, TTrigger> Add(TState from, TTrigger trigger, TState to, int number)
    {
        Put(from, trigger, new JobTransition<TState>(number, to, Ends: false, ToPrevious: false));
        return this;
    }

    /// <summary>从几个状态（标准里的超状态）收到同一个触发都去 to。</summary>
    public TransitionTable<TState, TTrigger> Add(IEnumerable<TState> from, TTrigger trigger, TState to, int number)
    {
        foreach (var state in from)
        {
            Add(state, trigger, to, number);
        }

        return this;
    }

    /// <summary>结束的转换：转完 Job 就没了，last 是结束前报的最后状态值。</summary>
    public TransitionTable<TState, TTrigger> AddEnd(TState from, TTrigger trigger, TState last, int number)
    {
        Put(from, trigger, new JobTransition<TState>(number, last, Ends: true, ToPrevious: false));
        return this;
    }

    /// <summary>回到之前记下的状态（从几个状态出发）。</summary>
    public TransitionTable<TState, TTrigger> AddResume(IEnumerable<TState> from, TTrigger trigger, int number)
    {
        foreach (var state in from)
        {
            Put(state, trigger, new JobTransition<TState>(number, state, Ends: false, ToPrevious: true));
        }

        return this;
    }

    /// <summary>当前状态收到这个触发能不能转、转到哪。</summary>
    public bool TryGet(TState from, TTrigger trigger, out JobTransition<TState> transition)
    {
        return _rows.TryGetValue((from, trigger), out transition);
    }

    /// <summary>当前状态收不收这个触发。</summary>
    public bool Allows(TState from, TTrigger trigger)
    {
        return _rows.ContainsKey((from, trigger));
    }

    private void Put(TState from, TTrigger trigger, JobTransition<TState> transition)
    {
        if (!_rows.TryAdd((from, trigger), transition))
        {
            throw new InvalidOperationException($"转换表重复：{from} 收到 {trigger} 已经有一条了（#{_rows[(from, trigger)].Number}）");
        }
    }
}
