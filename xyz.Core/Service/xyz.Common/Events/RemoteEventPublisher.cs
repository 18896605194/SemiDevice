using xyz.Tools;

namespace xyz.Common.Events;

/// <summary>
/// 跨进程事件的发送方（后端）：元件、模块调 Send 推给连着的客户端
/// </summary>
public static class RemoteEventPublisher
{
    #region 表：黑板、收件人，共用一把锁

    private static readonly object Gate = new();

    /// <summary>
    /// 后端的黑板：客户端刚连上时整块重放
    /// </summary>
    private static readonly RetainedBoard Board = new();

    private static List<Action<EventMessage>> _sentListeners = new();

    #endregion

    #region 死信：交给收件人时出错报出来

    /// <summary>
    /// 死信：收件人抛异常时触发
    /// </summary>
    public static event Action<EventMessage, Exception>? DeadLetter;

    #endregion

    #region 发消息：业务代码调 Send，推给所有连着的客户端

    /// <summary>
    /// 发状态类消息（默认）：写黑板，刚连上的客户端、后打开的页面都能马上拿到最新一条
    /// </summary>
    public static void Send<TMessage>(TMessage message, string token = "") where TMessage : class
    {
        Send(message, token, retain: true);
    }

    /// <summary>
    /// 发消息：retain=false 是通知类，不写黑板（同一格的旧内容擦掉），只发给当时连着的客户端
    /// </summary>
    public static void Send<TMessage>(TMessage message, string token, bool retain) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        token ??= string.Empty;

        #region 打包：消息转成 JSON 装进信封

        var envelope = Pack(message, token, retain);

        #endregion

        #region 写黑板、取收件人：在锁里做，真正发在锁外

        List<Action<EventMessage>> listeners;
        lock (Gate)
        {
            Board.Remember(envelope);
            listeners = _sentListeners;
        }

        #endregion

        #region 发给每个收件人：一个收件人就是一台连着的客户端，出错进死信

        foreach (var listener in listeners)
        {
            try
            {
                listener(envelope);
            }
            catch (Exception ex)
            {
                DeadLetter?.Invoke(envelope, ex);
            }
        }

        #endregion
    }

    #endregion

    #region EventService 用：登记收件人、取黑板快照

    /// <summary>
    /// 只给 EventService 用：拿到每一条新发出的消息，转给连着的客户端
    /// </summary>
    public static IDisposable SubscribeSent(Action<EventMessage> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (Gate)
        {
            _sentListeners = new List<Action<EventMessage>>(_sentListeners) { listener };
        }

        return new Subscription(() =>
        {
            lock (Gate)
            {
                var list = new List<Action<EventMessage>>(_sentListeners);
                list.Remove(listener);
                _sentListeners = list;
            }
        });
    }

    /// <summary>
    /// 只给 EventService 用：黑板的快照，客户端刚连上时整块重放
    /// </summary>
    public static IReadOnlyList<EventMessage> GetRetained()
    {
        lock (Gate)
        {
            return Board.Snapshot();
        }
    }

    #endregion

    #region 小方法：打包

    /// <summary>
    /// 打包：消息转成 JSON 装进信封，带上类型名、token、时间和要不要写黑板
    /// </summary>
    private static EventMessage Pack<TMessage>(TMessage message, string token, bool retain) where TMessage : class
    {
        return new EventMessage
        {
            TypeName = typeof(TMessage).FullName ?? string.Empty,
            Token = token,
            Payload = JsonHelper.Serialize(message),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Retain = retain,
        };
    }

    #endregion
}
