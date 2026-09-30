using System.Reactive.Concurrency;
using System.Reactive.Linq;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Service.Charts;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Service.Events;

/// <summary>
/// 实时曲线推送桥：实时曲线组件每来一行，这里转成一帧经事件流推给客户端（不留存，只给在线的）。
/// 组件在组件层、不引用契约层，所以这座桥搭在服务层——跟 IO、报警、日志那几条一个路子。
/// 推送挪到线程池上排队做：客户端慢了只是这边排队，不拖采样线程。
/// </summary>
public static class RealChartPublisher
{
    private static IDisposable? _subscription;

    private static string? _lastFault;

    /// <summary>
    /// 实时曲线接上采样之后调一次；没装实时曲线就不推。
    /// </summary>
    public static void Start()
    {
        var chart = RealChartComponent.Current;
        if (chart is null)
        {
            LogHelper.Warn("RealChart", "sc.xml 没配 RealChart 节点：实时曲线页没有数据");
            return;
        }

        _subscription?.Dispose();
        _subscription = chart.Frames
            .ObserveOn(TaskPoolScheduler.Default)
            .Subscribe(record => Publish(record.ToFrame(chart.Session)));
    }

    /// <summary>
    /// 一帧推失败不能把订阅弄断（Rx 里订阅方一抛异常整条流就停了），兜住；同样的错只记一次。
    /// </summary>
    private static void Publish(RealChartFrameDto frame)
    {
        try
        {
            EventBus.Send(frame, RealChartFrameDto.EventToken, retain: false);
            _lastFault = null;
        }
        catch (Exception exception)
        {
            if (_lastFault != exception.Message)
            {
                _lastFault = exception.Message;
                LogHelper.Warn("RealChart", $"实时曲线推送失败: {exception.Message}");
            }
        }
    }
}
