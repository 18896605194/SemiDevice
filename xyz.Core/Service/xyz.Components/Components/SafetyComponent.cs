using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 安全：整机的安全信号——急停、维修门、漏液、烟感、厂务气源 / 排风这类不归哪个模块管的 DI、AI。
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
