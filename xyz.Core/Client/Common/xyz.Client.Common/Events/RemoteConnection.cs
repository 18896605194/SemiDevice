using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Common.Events;

/// <summary>
/// 客户端连后端的事件线：保持一条推送流、断线 3 秒重连，收到的消息转到界面线程交给 RemoteEventBus
/// </summary>
public static class RemoteConnection
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

        // 事件流泵是长驻任务，不用 Task.Run 占线程池：
        // 用 LongRunning 起独立线程，并保留任务句柄，异常退出时能记日志。
        _pumpTask = Task.Factory
            .StartNew(RunLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .Unwrap();

        _pumpTask.ContinueWith(
            task => ClientLog.Error(nameof(RemoteConnection), $"事件流泵异常退出：{task.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static async Task RunLoop()
    {
        while (true)
        {
            try
            {
                var service = GrpcClientFactory.Create<IEventService>();
                var stream = service.SubscribeAsync(new EventSubscription());

                // 调 SubscribeAsync 时还没真正连上（后端不在线要等枚举时才抛），收到第一条才算连上；
                // 后端开流先发一条空信封作标记，标记本身不投递。
                await foreach (var envelope in stream)
                {
                    SetConnected(true);
                    if (!string.IsNullOrEmpty(envelope.TypeName))
                    {
                        DeliverOnUi(envelope);
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
            RemoteEventBus.Deliver(envelope);
        }
        else
        {
            _uiContext.Post(_ => RemoteEventBus.Deliver(envelope), null);
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
