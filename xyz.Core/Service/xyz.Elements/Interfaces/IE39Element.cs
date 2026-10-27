using xyz.Secs.Gem.E39;

namespace xyz.Elements.Interfaces;

/// <summary>
/// E39 对象服务
/// </summary>
public interface IE39Element
{
    /// <summary>
    /// 接到链路和 GEM 上
    /// </summary>
    void Attach(IHsms link, IE30Element gem);

    /// <summary>
    /// 对象服务的协议部分（xyz.Secs）：别的标准往里登记对象类型，EDA 按类型名找对象；没接链路为 null
    /// </summary>
    E39Engine? Engine { get; }
}
