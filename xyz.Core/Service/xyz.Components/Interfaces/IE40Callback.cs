using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// E40（PJ）的设备侧上报口：PJ 的状态转换由 JobManager 调这里，EAP 侧据此发 S6F11（每条转换一个 CEID）。
/// 片开始、结束加工 E40 没有事件，由 E90 照晶圆账（片的工艺状态）报。
/// 实现由 EAP 侧提供并挂到 JobManager.E40Callback；没接 EAP 时为 null，照常跑。
/// 所有上报（E87 / E84 / E90 / E40 / E94）都在 EAP 的派发组件（EapNotifierComponent）一条线程上按发生顺序串行调用（不占扫描线程）：实现里可以慢，但不要死等。
/// </summary>
public interface IE40Callback
{
    /// <summary>
    /// PJ 状态转换（E40 #1~#18）：transition 是第几号转换，job.State 是转到的状态（#7 / #16 / #17 / #18 转完 PJ 就结束了，
    /// 这时是结束前的最后状态值）。
    /// </summary>
    void ProcessJobStateChanged(ProcessJobDto job, int transition);
}
