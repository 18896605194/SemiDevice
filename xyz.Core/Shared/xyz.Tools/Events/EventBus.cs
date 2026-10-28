namespace xyz.Tools;

/// <summary>
/// 进程内事件总线：只在本进程里传，跨进程用 RemoteEventPublisher / RemoteEventSubscriber
/// </summary>
public static class EventBus
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(Type Type, string Token), List<Action<object>>> Handlers = new();

    /// <summary>
    /// 订阅者回调抛了异常：不耽误别的订阅者和发送方，在这里报出来
    /// </summary>
    public static event Action<object, Exception>? DeadLetter;

    /// <summary>
    /// 发布：在调用线程上依次调本进程的订阅者，传的是原对象
    /// </summary>
    public static void Send<TMessage>(TMessage message, string token = "") where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(message);
        token ??= string.Empty;

        Action<object>[] handlers;
        lock (Gate)
        {
            if (!Handlers.TryGetValue((typeof(TMessage), token), out var list))
            {
                return;
            }

            // 给副本：回调里退订、别的线程这时订阅都不影响这一轮
            handlers = list.ToArray();
        }

        foreach (var handler in handlers)
        {
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                DeadLetter?.Invoke(message, ex);
            }
        }
    }

    /// <summary>
    /// 订阅全局消息（token 为空串）
    /// </summary>
    public static IDisposable Register<TMessage>(Action<TMessage> handler) where TMessage : class
    {
        return Register<TMessage>(string.Empty, handler);
    }

    /// <summary>
    /// 按 token 订阅：token 区分同类型消息的不同实例；订阅之前发的收不到
    /// </summary>
    public static IDisposable Register<TMessage>(string token, Action<TMessage> handler) where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        token ??= string.Empty;

        var key = (typeof(TMessage), token);
        Action<object> wrapped = message => handler((TMessage)message);
        lock (Gate)
        {
            if (!Handlers.TryGetValue(key, out var list))
            {
                list = new List<Action<object>>();
                Handlers[key] = list;
            }

            list.Add(wrapped);
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
}
