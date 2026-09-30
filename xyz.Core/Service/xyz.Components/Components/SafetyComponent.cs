using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 安全：整机的安全信号——急停、维修门、漏液、烟感、厂务气源 / 排风这类不归哪个模块管的 DI、AI。
/// sc.xml 的 Safety 节点下一个信号一个子节点：DI 用 DiSensorComponent（DiIndex 点号、TriggerLevel 报警电平、AlarmEnabled 是否报警），
/// AI 用 AiSensorComponent（AiIndex 点号，上下限、预警带、防抖在 ec.xml）。
/// 它不是模块，装配完由宿主给它起一条自己的扫描线程，子节点按周期读点、判报警。
/// 绑在这里的点跟模块里绑的一样进数据曲线（库里记、勾选树里有），IO 页单独一页。
/// </summary>
[Component(description: "安全信号（急停、维修门、漏液、厂务气源/排风等整机 DI/AI）")]
public class SafetyComponent : ComponentBase
{
    /// <summary>
    /// 当前安全组件；sc.xml 没配 Safety 节点时为 null。
    /// </summary>
    public static SafetyComponent? Current { get; set; }

    public SafetyComponent()
    {
        Current = this;
    }
}
