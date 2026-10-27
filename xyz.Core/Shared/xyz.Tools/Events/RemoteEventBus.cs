using System.Text.Json;

namespace xyz.Tools;

/// <summary>
/// 跨进程事件总线：后端 Send 推给客户端，客户端 Register 收；进程内用 EventBus
/// </summary>
public static class RemoteEventBus
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(string TypeName, string Token), List<Action<object>>> Handlers = new();
    private static readonly Dictionary<string, Type> MessageTypes = new();

    /// <summary>
    /// 黑板：每个（类型，token）只留最后一条状态类消息；后端给刚连上的客户端重放，客户端给后订阅的页面补发
    /// </summary>
    private static readonly Dictionary<(string TypeName, string Token), EventMessage> Retained = new();

    private static List<Action<EventMessage>> _sentListeners = new();

    /// <summary>
    /// 死信：消息解不开或订阅者回调抛异常时触发，用来查两头消息类型对不上这类问题
    /// </summary>
    public static event Action<EventMessage, Exception>? DeadLetter;

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

        var envelope = Pack(message, token, retain);
        List<Action<EventMessage>> listeners;
        lock (Gate)
        {
            Remember(envelope);
            listeners = _sentListeners;
        }

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
    }

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

        var type = typeof(TMessage);
        var key = (type.FullName!, token);
        Action<object> wrapped = message => handler((TMessage)message);

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

        // 补发：页面比消息晚订阅，也能马上拿到当前状态
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
    }

    /// <summary>
    /// 只给后端 EventService 用：拿到每一条新发出的消息，转给连着的客户端
    /// </summary>
    public static IDisposable SubscribeSent(Action<EventMessage> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        // 换整张表，不改旧表：Send 在锁外遍历的是它拿到的那张
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

    /// <summary>
    /// 只给客户端 RemoteConnection 用：把收到的消息送进来，写黑板、调订阅者
    /// </summary>
    public static void Deliver(EventMessage envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // protobuf 传输会省略默认值字段，空 token 在接收端为 null，这里归一化回空串
        envelope.TypeName ??= string.Empty;
        envelope.Token ??= string.Empty;

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
    }

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
}
