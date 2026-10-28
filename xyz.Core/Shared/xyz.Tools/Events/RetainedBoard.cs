using System.Diagnostics.CodeAnalysis;

namespace xyz.Tools;

/// <summary>
/// 黑板：每个（类型，token）只留最后一条状态类消息；自己不带锁，用的地方在锁里调
/// </summary>
public sealed class RetainedBoard
{
    private readonly Dictionary<(string TypeName, string Token), EventMessage> _retained = new();

    /// <summary>
    /// 写黑板：状态类盖掉同一格，通知类把同一格擦掉
    /// </summary>
    public void Remember(EventMessage envelope)
    {
        var key = (envelope.TypeName, envelope.Token);
        if (envelope.Retain)
        {
            _retained[key] = envelope; // 往黑板上面去存
        }
        else
        {
            _retained.Remove(key); // 移除
        }
    }

    /// <summary>
    /// 取这一格的最后一条
    /// </summary>
    public bool TryGet((string TypeName, string Token) key, [MaybeNullWhen(false)] out EventMessage envelope)
    {
        return _retained.TryGetValue(key, out envelope);
    }

    /// <summary>
    /// 整块抄一份
    /// </summary>
    public List<EventMessage> Snapshot()
    {
        return _retained.Values.ToList();
    }
}
