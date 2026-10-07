using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// E40（PJ）的设备侧上报口：PJ 的状态转换、片开始和结束加工由 JobManager 调这里，EAP 侧据此发 S6F11（每条转换一个 CEID）。
/// 实现由 EAP 侧提供并挂到 JobManager.E40Callback；没接 EAP 时为 null，照常跑。
/// 跟 E94 回调共用一条派发线程、按发生顺序串行调用（不占扫描线程）：实现里可以慢，但不要死等。
/// </summary>
public interface IE40Callback
{
    /// <summary>
    /// PJ 状态转换（E40 #1~#18）：transition 是第几号转换，job.State 是转到的状态（#7 / #16 / #17 / #18 转完 PJ 就结束了，
    /// 这时是结束前的最后状态值）。
    /// </summary>
    void ProcessJobTransitioned(ProcessJobDto job, int transition);

    /// <summary>一片在某站开始加工。</summary>
    void WaferProcessStarted(ProcessJobDto job, JobWaferDto wafer, string station);

    /// <summary>一片在某站加工结束（成没成看 success）。</summary>
    void WaferProcessEnded(ProcessJobDto job, JobWaferDto wafer, string station, bool success);
}
