namespace xyz.Tools;

/// <summary>
/// 进程内静态事件总线，写法与 CommunityToolkit.Mvvm 的 Messenger 一致：
/// 消息定义为普通 class/record，EventBus.Send 发布、EventBus.Register 订阅（返回 IDisposable 注销）。
/// 跨进程由基础设施桥接（后端 EventService / 客户端 RemoteEventBus），业务代码两端写法完全相同。
/// 路由键 = (TypeName, Token)：token 为空串表示该类型的全局消息；
/// 同一类型用不同 token 区分不同实例（如 "LoadPort1"/"LoadPort2"）。
/// Send 在调用线程同步派发；跨进程消息由 RemoteEventBus 投递到 UI 线程后再派发。
/// </summary>
public static class EventBus
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(string TypeName, string Token), List<Action<object>>> Handlers = new();
    private static readonly Dictionary<string, Type> MessageTypes = new();
    private static readonly Dictionary<(string TypeName, string Token), EventMessage> Retained = new();
    private static List<Action<EventMessage>> _rawSubscribers = new();

    /// <summary>
    /// 死信：反序列化失败或订阅者回调抛异常时触发，用于排查消息类型不一致等问题。
    /// </summary>
    public static event Action<EventMessage, Exception>? DeadLetter;

    /// <summary>
    /// 发布留存消息（状态类，默认）：每键保留最后一条，
    /// 新订阅者 Register 时立即补发、客户端重连时由服务端重放。
    /// </summary>
    public static void Send<TMessage>(TMessage message, string token = "") where TMessage : class
    {
        Send(message, token, retain: true);
    }

    /// <summary>
    /// 发布消息。retain=false 为发生类消息：只派发给当时在线的订阅者，不留存、不补发。
    /// </summary>
    public static void Send<TMessage>(TMessage message, string token, bool retain) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);

        var type = typeof(TMessage);
        var typeName = type.FullName!;
        token ??= string.Empty;
        var envelope = EventEnvelope.Of(message, token, retain);

        List<Action<object>>? typed;
        List<Action<EventMessage>> raw;
        lock (Gate)
        {
            MessageTypes[typeName] = type;
            if (retain)
            {
                Retained[(typeName, token)] = envelope;
            }
            else
            {
                Retained.Remove((typeName, token));
            }

            Handlers.TryGetValue((typeName, token), out typed);
            raw = _rawSubscribers;
        }

        // 本进程订阅者直接拿原对象，不经序列化往返
        Dispatch(typed, message, envelope);
        // 桥接订阅者（gRPC 服务端流）拿信封转发
        foreach (var subscriber in raw)
        {
            try { subscriber(envelope); }
            catch (Exception ex) { DeadLetter?.Invoke(envelope, ex); }
        }
    }

    /// <summary>
    /// 订阅全局消息（token 为空串）。
    /// </summary>
    public static IDisposable Register<TMessage>(Action<TMessage> handler) where TMessage : class
    {
        return Register<TMessage>(string.Empty, handler);
    }

    /// <summary>
    /// 按 token 订阅消息。token 区分同类型消息的不同实例。
    /// 若该键已有留存消息（状态类），订阅后立即补发最后一条。
    /// </summary>
    public static IDisposable Register<TMessage>(string token, Action<TMessage> handler) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        token ??= string.Empty;

        var type = typeof(TMessage);
        var typeName = type.FullName!;
        var key = (typeName, token);
        Action<object> wrapped = message => handler((TMessage)message);

        EventMessage? replay;
        lock (Gate)
        {
            MessageTypes[typeName] = type;
            if (!Handlers.TryGetValue(key, out var list))
            {
                Handlers[key] = list = new List<Action<object>>();
            }
            list.Add(wrapped);
            Retained.TryGetValue(key, out replay);
        }

        // 补发留存：ViewModel 晚于流泵注册时也能立即拿到当前状态
        if (replay is not null)
        {
            try
            {
                handler((TMessage)EventEnvelope.From(replay, type));
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
    /// 基础设施专用：把远端信封投递进本进程总线。
    /// Retain=true 的消息进入本进程留存（晚注册的订阅者可补发）；
    /// 只反序列化本地注册过的类型，按 (TypeName, Token) 派发给本地订阅者；
    /// 不通知桥接订阅者（避免跨进程回声）。
    /// </summary>
    public static void Deliver(EventMessage envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // protobuf 传输会省略默认值字段，空 token 在接收端为 null，这里归一化回空串
        envelope.TypeName ??= string.Empty;
        envelope.Token ??= string.Empty;

        List<Action<object>>? typed = null;
        lock (Gate)
        {
            if (envelope.Retain)
            {
                Retained[(envelope.TypeName, envelope.Token)] = envelope;
            }

            if (MessageTypes.TryGetValue(envelope.TypeName, out _)
                && Handlers.TryGetValue((envelope.TypeName, envelope.Token), out var list))
            {
                typed = list;
            }
        }

        if (typed is null) return;

        object message;
        try
        {
            message = EventEnvelope.From(envelope, MessageTypes[envelope.TypeName]);
        }
        catch (Exception ex)
        {
            DeadLetter?.Invoke(envelope, ex);
            return;
        }

        Dispatch(typed, message, envelope);
    }

    /// <summary>
    /// 基础设施专用：桥接订阅，收到本进程所有 Send 出来的信封（不含 Deliver 进来的远端消息）。
    /// 服务端 EventService 用它把事件转发给客户端流。
    /// </summary>
    public static IDisposable SubscribeRaw(Action<EventMessage> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        lock (Gate)
        {
            var list = new List<Action<EventMessage>>(_rawSubscribers) { subscriber };
            _rawSubscribers = list;
        }

        return new Subscription(() =>
        {
            lock (Gate)
            {
                _rawSubscribers = new List<Action<EventMessage>>(_rawSubscribers);
                _rawSubscribers.Remove(subscriber);
            }
        });
    }

    /// <summary>
    /// 基础设施专用：当前留存（每键最后一条）的快照，供新客户端连接时重放。
    /// </summary>
    public static IReadOnlyList<EventMessage> GetRetained()
    {
        lock (Gate)
        {
            return Retained.Values.ToList();
        }
    }

    private static void Dispatch(List<Action<object>>? typed, object message, EventMessage envelope)
    {
        if (typed is null) return;
        foreach (var handler in typed)
        {
            try { handler(message); }
            catch (Exception ex) { DeadLetter?.Invoke(envelope, ex); }
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }
}
