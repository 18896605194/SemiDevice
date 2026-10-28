using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 整机安全信号
/// </summary>
[Ioc]
public interface ISafety
{
    /// <summary>
    /// 元件名（IO 页的页签名）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 下面配了信号没有
    /// </summary>
    bool HasSignals { get; }
}
