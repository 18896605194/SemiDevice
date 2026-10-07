using System.Threading.Channels;
using xyz.Common.Log;

namespace xyz.Components.Components;

/// <summary>
/// EAP 回调派发：设备侧要报给 EAP 的事放进来，专用线程按入队顺序一条条调出去。
/// 不占扫描线程、不拿模块锁：EAP 侧发 SECS 卡住时不会拖住设备；第一次真入队才起线程（没接 EAP 的机台不多这个线程）；
/// 积压只告警不丢（EAP 事件丢了比慢更糟）；单条出异常只记日志。跟 LoadPort 的 E87 / E84 派发是同一个做法。
/// </summary>
public sealed class EapNotifier
{
    /// <summary>积压到这个条数的整数倍时记一次告警。</summary>
    private const int BacklogWarning = 500;

    private readonly Func<string> _owner;
    private readonly Channel<Action> _queue = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private int _started;
    private int _pending;

    /// <param name="owner">日志里写谁的回调（组件名；装配后才定，所以给个取名字的委托）。</param>
    public EapNotifier(Func<string> owner)
    {
        _owner = owner;
    }

    /// <summary>投递一条回调（任意线程可调）。</summary>
    public void Post(Action notification)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
        {
            _ = Task.Run(DispatchAsync);
        }

        if (!_queue.Writer.TryWrite(notification))
        {
            return;
        }

        int pending = Interlocked.Increment(ref _pending);
        if (pending > 0 && pending % BacklogWarning == 0)
        {
            LogHelper.Warn(_owner(), $"EAP 回调积压 {pending} 条，检查 EAP 侧是否卡住");
        }
    }

    private async Task DispatchAsync()
    {
        await foreach (var notification in _queue.Reader.ReadAllAsync())
        {
            Interlocked.Decrement(ref _pending);
            try
            {
                notification();
            }
            catch (Exception exception)
            {
                LogHelper.Warn(_owner(), $"EAP 回调异常: {exception.Message}");
            }
        }
    }
}
