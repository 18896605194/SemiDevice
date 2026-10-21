using xyz.Components.Components;

namespace xyz.Components.Interfaces;

/// <summary>
/// E39 对象服务
/// </summary>
public interface IE39Component
{
    /// <summary>
    /// 接到链路和 GEM 上
    /// </summary>
    void Attach(IHsms link, IE30Component gem);

    /// <summary>
    /// 别的标准往里登记一种对象（PJ、CJ、片、位置）
    /// </summary>
    internal void Register(IE39ObjectType type);
}
