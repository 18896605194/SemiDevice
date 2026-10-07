using System.Threading.Channels;
using xyz.Common.Log;
using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// EAP 上报派发（sc.xml Eap 节点下的 Notifier）：设备侧要报给 EAP 的事（LoadPort 的 E87 / E84、晶圆账的 E90、Job 管理的 E40 / E94）都放进来，
/// 整个 EAP 一条专用线程按入队顺序一条条调出去——不同来源的事报给 Host 的先后，就是它们发生的先后。
/// 不占扫描线程、不拿模块锁：EAP 侧发 SECS 卡住时不会拖住设备；第一次真入队才起线程；积压只告警不丢（EAP 事件丢了比慢更糟）；单条出异常只记日志。
/// 设备侧只在上报口挂上（不为 null）时投递；Eap 下没配这个节点 EAP 就不接（见 <see cref="EapComponent.Bind"/>），上报口一直是 null。
/// </summary>
[Component(description: "EAP 上报派发：设备侧报给 EAP 的事按发生先后在一条线程上发")]
public class EapNotifierComponent : ComponentBase
{
    /// <summary>积压到这个条数的整数倍时记一次告警。</summary>
    private const int BacklogWarning = 500;

    private readonly Channel<Action> _queue = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private int _started;
    private int _pending;

    /// <summary>当前派发组件；Eap 节点下没配 Notifier 时为 null（这时 EAP 不接，也没有要报的）。</summary>
    public static EapNotifierComponent? Current { get; set; }

    public EapNotifierComponent()
    {
        Current = this;
    }

    /// <summary>投递一条上报（任意线程可调）。</summary>
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
            LogHelper.Warn(Name, $"EAP 上报积压 {pending} 条，检查 EAP 侧是否卡住");
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
                LogHelper.Warn(Name, $"EAP 上报出错：{exception.Message}");
            }
        }
    }
}
