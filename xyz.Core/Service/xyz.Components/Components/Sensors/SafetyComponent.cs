using xyz.Components.Attributes;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// 安全：整机的安全信号——急停、维修门、漏液、烟感、厂务气源 / 排风这类不归哪个模块管的 DI、AI。
/// </summary>
[Component(description: "安全信号（急停、维修门、漏液、厂务气源/排风等整机 DI/AI）")]
public class SafetyComponent : ComponentBase, ISafetyComponent
{
    public SafetyComponent()
    {
        Register<ISafetyComponent>(this);
    }

    /// <summary>
    /// 下面配了信号没有（没配就不给它单独一页 IO）
    /// </summary>
    public bool HasSignals => Children.Count > 0;
}
