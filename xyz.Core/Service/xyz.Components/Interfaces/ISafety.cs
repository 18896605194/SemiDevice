namespace xyz.Components.Interfaces;

/// <summary>
/// 整机安全信号
/// </summary>
public interface ISafety
{
    /// <summary>
    /// 组件名（IO 页的页签名）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 下面配了信号没有
    /// </summary>
    bool HasSignals { get; }
}
