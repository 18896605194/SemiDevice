using xyz.Elements.Models;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 收元件报的事件（GEM 的 Collection Event）：E30 拿去发 S6F11，EDA 拿去按采集计划采。
/// 元件经 ElementBase.RaiseEvent 报，一次发给全部收事件的元件；要不要、怎么报各自定
/// </summary>
public interface IEventReceiver
{
    /// <summary>
    /// 收一个事件：code 是 source 元件上 [EventAttribut] 的代码，data 是事件带的 DV（[DataVariable] 的代码 → 这一刻的值）。
    /// 任意线程调（扫描线程上也会调），不能抛异常、不能等
    /// </summary>
    void Report(ElementBase source, string code, params GemData[] data);
}
