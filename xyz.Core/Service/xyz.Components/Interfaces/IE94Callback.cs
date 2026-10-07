using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// E94（CJ）的设备侧上报口：CJ 的状态转换由 JobManager 调这里，EAP 侧据此发 S6F11（每条转换一个 CEID）。
/// 实现由 EAP 侧提供并挂到 JobManager.E94Callback；没接 EAP 时为 null，照常跑。
/// 跟 E40 回调共用一条派发线程、按发生顺序串行调用：一个 PJ 结束（E40 #7）一定先于它的 CJ 完成（E94 #10）报出去。
/// </summary>
public interface IE94Callback
{
    /// <summary>
    /// CJ 状态转换（E94 #1~#13）：transition 是第几号转换，job.State 是转到的状态（#2 / #13 转完 CJ 就删了，这时是删之前的最后状态值）。
    /// </summary>
    void ControlJobTransitioned(ControlJobDto job, int transition);
}
