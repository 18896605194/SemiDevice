using xyz.Client.DataModels.Rpc;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.DataModels.Events;

/// <summary>
/// 客户端事件流泵：与后端 IEventService 保持一条服务端流，
/// 收到的信封统一投递到 UI 线程再灌进本进程 EventBus，ViewModel 回调天然在 UI 线程。
/// 断线自动重连（3s），重连成功即重放留存消息。
/// </summary>
public static class RemoteEventBus
{
    private const int ReconnectDelayMs = 3000;

    private static SynchronizationContext? _uiContext;
    private static int _connected;

    /// <summary>
    /// 后端事件流是否在线（供界面显示通讯状态灯）。
    /// </summary>
    public static bool IsConnected => Volatile.Read(ref _connected) == 1;

    /// <summary>
    /// 连接状态变化（true=已连上，false=断开）。在 UI 线程触发。
    /// </summary>
    public static event Action<bool>? ConnectionChanged;

    /// <summary>
    /// App 启动时（GrpcClientFactory.Initialize 之后）调用一次：捕获 UI 线程上下文并启动流泵。
    /// </summary>
    public static void Initialize()
    {
        if (_uiContext is not null) return;
        _uiContext = SynchronizationContext.Current;
        Task.Run(RunLoop);
    }

    /// <summary>
    /// 客户端 → 后端上行发布。进入后端进程内总线，后端本地订阅者可收到。
    /// </summary>
    public static void SendToServer<TMessage>(TMessage message, string token = "") where TMessage : class
    {
        var envelope = EventEnvelope.Of(message, token);
        Task.Run(async () =>
        {
            try
            {
                var service = GrpcClientFactory.Create<IEventService>();
                await service.PublishAsync(envelope);
            }
            catch
            {
                // 后端不在线时上行静默丢弃；需要确认送达的业务应由后端回发事件确认
            }
        });
    }

    private static async Task RunLoop()
    {
        while (true)
        {
            try
            {
                var service = GrpcClientFactory.Create<IEventService>();
                var stream = service.SubscribeAsync(new EventSubscription());
                SetConnected(true);

                await foreach (var envelope in stream)
                {
                    DeliverOnUi(envelope);
                }
            }
            catch
            {
                // 断线：等 3s 重连，重连成功后服务端会重放留存消息
            }

            SetConnected(false);
            await Task.Delay(ReconnectDelayMs);
        }
    }

    private static void DeliverOnUi(EventMessage envelope)
    {
        if (_uiContext is null)
        {
            EventBus.Deliver(envelope);
        }
        else
        {
            _uiContext.Post(_ => EventBus.Deliver(envelope), null);
        }
    }

    private static void SetConnected(bool value)
    {
        var flag = value ? 1 : 0;
        if (Interlocked.Exchange(ref _connected, flag) == flag) return;

        if (_uiContext is null)
        {
            ConnectionChanged?.Invoke(value);
        }
        else
        {
            _uiContext.Post(_ => ConnectionChanged?.Invoke(value), null);
        }
    }
}
