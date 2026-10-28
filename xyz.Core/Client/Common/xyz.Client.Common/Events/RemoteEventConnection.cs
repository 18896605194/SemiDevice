using xyz.Client.Common.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Common.Events;

/// <summary>
/// 客户端连后端的事件
/// </summary>
public static class RemoteEventConnection
{
    private const int ReconnectDelayMs = 3000;

    private static SynchronizationContext? _uiContext;
    private static Task? _pumpTask;
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
        if (_pumpTask is not null)
        {
            return;
        }

        _uiContext = SynchronizationContext.Current;

        _pumpTask = Task.Run(RunLoop);
    }

    private static async Task RunLoop()
    {
        while (true)
        {
            try
            {
                var service = GrpcClientFactory.Create<IEventService>();  //获取后台的服务
                var stream = service.SubscribeAsync();  //告诉后端有消息就推送过来
                await foreach (var envelope in stream)
                {
                    SetConnected(true);
                    if (!string.IsNullOrEmpty(envelope.TypeName))
                    {
                        DeliverOnUi(envelope);  //推给每一个界面的订阅的
                    }
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
            RemoteEventSubscriber.Deliver(envelope);
        }
        else
        {
            _uiContext.Post(_ => RemoteEventSubscriber.Deliver(envelope), null);
        }
    }

    private static void SetConnected(bool value)
    {
        var flag = value ? 1 : 0;
        if (Interlocked.Exchange(ref _connected, flag) == flag)
        {
            return;
        }

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
