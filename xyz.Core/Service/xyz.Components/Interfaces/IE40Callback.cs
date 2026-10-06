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
    /// PJ 状态转换（E40 #1~#18）：from 为 null 是刚建好（#1）；to 为 null 是转完 PJ 就结束了（标准里的 no state，#7 / #16 / #17 / #18），
    /// 这时 job.State 是结束前的最后状态值。
    /// </summary>
    void ProcessJobTransitioned(ProcessJobDto job, int transition, PrJobState? from, PrJobState? to);

    /// <summary>一片在某站开始加工。</summary>
    void WaferProcessStarted(ProcessJobDto job, JobWaferDto wafer, string station);

    /// <summary>一片在某站加工结束（成没成看 success）。</summary>
    void WaferProcessEnded(ProcessJobDto job, JobWaferDto wafer, string station, bool success);
}
