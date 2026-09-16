namespace xyz.Components.Wafers;

/// <summary>
/// 片在槽位里的物理状态，来自 Mapping 结果或人工建片。
/// </summary>
public enum WaferStatus
{
    /// <summary>正常一片。</summary>
    Normal = 0,

    /// <summary>交叉片（跨槽）。</summary>
    Crossed = 1,

    /// <summary>叠片（一个槽两片）。</summary>
    Double = 2,

    /// <summary>陪片。</summary>
    Dummy = 3,

    /// <summary>有片但状态不明。</summary>
    Unknown = 4,
}
