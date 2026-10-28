using System.Text.Json;

namespace xyz.Tools;

/// <summary>
/// 跨进程事件总线：后端 Send 推给客户端，客户端 Register 收；进程内用 EventBus
/// </summary>
public static class RemoteEventBus
{
    #region 表：订阅、类型、黑板、收件人，共用一把锁

    private static readonly object Gate = new();
    private static readonly Dictionary<(string TypeName, string Token), List<Action<object>>> Handlers = new();
    private static readonly Dictionary<string, Type> MessageTypes = new();

    /// <summary>
    /// 黑板：每个（类型，token）只留最后一条状态类消息；后端给刚连上的客户端重放，客户端给后订阅的页面补发
    /// </summary>
    private static readonly Dictionary<(string TypeName, string Token), EventMessage> Retained = new();

    private static List<Action<EventMessage>> _sentListeners = new();

    #endregion

    #region 死信：消息解不开、订阅者回调出错时报出来

    /// <summary>
    /// 死信：消息解不开或订阅者回调抛异常时触发，用来查两头消息类型对不上这类问题
    /// </summary>
    public static event Action<EventMessage, Exception>? DeadLetter;

    #endregion

    #region 后端发消息：业务代码调 Send，推给所有连着的客户端

    /// <summary>
    /// 后端发状态类消息（默认）：写黑板，刚连上的客户端、后打开的页面都能马上拿到最新一条
    /// </summary>
    public static void Send<TMessage>(TMessage message, string token = "") where TMessage : class
    {
        Send(message, token, retain: true);
    }

    /// <summary>
    /// 后端发消息：retain=false 是通知类，不写黑板（同一格的旧内容擦掉），只发给当时连着的客户端
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
            Remember(envelope);
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

    #region 客户端订消息：页面调 Register，按类型 + token 收

    /// <summary>
    /// 客户端订阅全局消息（token 为空串）
    /// </summary>
    public static IDisposable Register<TMessage>(Action<TMessage> handler) where TMessage : class
    {
        return Register<TMessage>(string.Empty, handler);
    }

    /// <summary>
    /// 客户端按 token 订阅：黑板上已有这一格的话，马上补发最后一条
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
            Retained.TryGetValue(key, out replay); // 从黑板中获取
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

    #region 后端 EventService 用：登记收件人、取黑板快照

    /// <summary>
    /// 只给后端 EventService 用：拿到每一条新发出的消息，转给连着的客户端
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
    /// 只给后端 EventService 用：黑板的快照，客户端刚连上时整块重放
    /// </summary>
    public static IReadOnlyList<EventMessage> GetRetained()
    {
        lock (Gate)
        {
            return Retained.Values.ToList();
        }
    }

    #endregion

    #region 客户端 RemoteConnection 用：收到的消息送进来，写黑板、分给订阅的页面

    /// <summary>
    /// 只给客户端 RemoteConnection 用：把收到的消息送进来，写黑板、调订阅者
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
            Remember(envelope); // 客户端写进黑板
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

    #region 小方法：写黑板、打包、拆包

    /// <summary>
    /// 写黑板（调用方持锁）：状态类盖掉同一格，通知类把同一格擦掉
    /// </summary>
    private static void Remember(EventMessage envelope)
    {
        var key = (envelope.TypeName, envelope.Token);
        if (envelope.Retain)
        {
            Retained[key] = envelope; // 往黑板上面去存
        }
        else
        {
            Retained.Remove(key); // 移除
        }
    }

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
