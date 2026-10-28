using System.Text.Json;
using xyz.Tools;

namespace xyz.Client.Common.Events;

/// <summary>
/// 跨进程事件的订阅方（客户端）：页面调 Register 订，RemoteEventConnection 收到的经 Deliver 送进来
/// </summary>
public static class RemoteEventSubscriber
{
    #region 表：订阅、类型、黑板，共用一把锁

    private static readonly object Gate = new();
    private static readonly Dictionary<(string TypeName, string Token), List<Action<object>>> Handlers = new();
    private static readonly Dictionary<string, Type> MessageTypes = new();

    /// <summary>
    /// 客户端的黑板：页面比消息晚订阅时补发最后一条
    /// </summary>
    private static readonly RetainedBoard Board = new();

    #endregion

    #region 死信：消息解不开、订阅者回调出错时报出来

    /// <summary>
    /// 死信：消息解不开或订阅者回调抛异常时触发，用来查两头消息类型对不上这类问题
    /// </summary>
    public static event Action<EventMessage, Exception>? DeadLetter;

    #endregion

    #region 订消息：页面调 Register，按类型 + token 收

    /// <summary>
    /// 订阅全局消息（token 为空串）
    /// </summary>
    public static IDisposable Register<TMessage>(Action<TMessage> handler) where TMessage : class
    {
        return Register<TMessage>(string.Empty, handler);
    }

    /// <summary>
    /// 按 token 订阅：黑板上已有这一格的话，马上补发最后一条
    /// </summary>
    public static IDisposable Register<TMessage>(string token, Action<TMessage> handler) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        token ??= string.Empty;

        #region 键和包装：各种类型的回调放在同一张表里，统一包成收 object 的

        var type = typeof(TMessage);
        var key = (type.FullName!, token);
        Action<object> wrapped = message => handler((TMessage)message);

        #endregion

        #region 登记：记下类型、加进这一格，顺便看黑板上有没有

        EventMessage? replay;
        lock (Gate)
        {
            MessageTypes[type.FullName!] = type;
            if (!Handlers.TryGetValue(key, out var list))
            {
                list = new List<Action<object>>();
                Handlers[key] = list;
            }

            list.Add(wrapped);
            Board.TryGet(key, out replay); // 从黑板中获取
        }

        #endregion

        #region 补发：页面比消息晚订阅，也能马上拿到当前状态

        if (replay is not null)
        {
            try
            {
                handler((TMessage)Unpack(replay, type));
            }
            catch (Exception ex)
            {
                DeadLetter?.Invoke(replay, ex);
            }
        }

        #endregion

        #region 退订凭据：Dispose 时从这一格删掉

        return new Subscription(() =>
        {
            lock (Gate)
            {
                if (Handlers.TryGetValue(key, out var list))
                {
                    list.Remove(wrapped);
                }
            }
        });

        #endregion
    }

    #endregion

    #region RemoteEventConnection 用：收到的消息送进来，写黑板、分给订阅的页面

    /// <summary>
    /// 只给 RemoteEventConnection 用：把收到的消息送进来，写黑板、调订阅者
    /// </summary>
    public static void Deliver(EventMessage envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        #region 归一化：protobuf 传输会省略默认值字段，空 token 在接收端为 null，换回空串

        envelope.TypeName ??= string.Empty;
        envelope.Token ??= string.Empty;

        #endregion

        #region 写黑板、查表：没人订的只留在黑板上；订了的复制一份，到锁外再调

        Type? type;
        Action<object>[] handlers;
        lock (Gate)
        {
            Board.Remember(envelope); // 客户端写进黑板
            if (!MessageTypes.TryGetValue(envelope.TypeName, out type)
                || !Handlers.TryGetValue((envelope.TypeName, envelope.Token), out var list))
            {
                return;
            }

            handlers = list.ToArray();
        }

        #endregion

        #region 拆包：JSON 转回消息，只拆一次，几个订阅者共用

        object message;
        try
        {
            message = Unpack(envelope, type);
        }
        catch (Exception ex)
        {
            DeadLetter?.Invoke(envelope, ex);
            return;
        }

        #endregion

        #region 挨个调：一个回调出错只进死信，不耽误别的

        foreach (var handler in handlers)
        {
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                DeadLetter?.Invoke(envelope, ex);
            }
        }

        #endregion
    }

    #endregion

    #region 小方法：拆包

    /// <summary>
    /// 拆包：信封里的 JSON 转回消息
    /// </summary>
    private static object Unpack(EventMessage envelope, Type type)
    {
        return JsonHelper.Deserialize(envelope.Payload, type)
               ?? throw new JsonException($"类型 {type.Name} 反序列化结果为 null");
    }

    #endregion
}
