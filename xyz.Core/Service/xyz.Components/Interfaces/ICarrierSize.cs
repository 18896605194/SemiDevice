namespace xyz.Components.Interfaces;

/// <summary>
/// 片盒尺寸检测（可选）：LoadPort 下配了尺寸传感器才有，没配就不查尺寸
/// </summary>
public interface ICarrierSize
{
    /// <summary>
    /// 每种尺寸的信号：键是尺寸（8 / 12），值是亮没亮；读不到、一种尺寸两个传感器对不上为 null
    /// </summary>
    IReadOnlyDictionary<int, bool?> Sensors { get; }

    /// <summary>
    /// 认出的片盒尺寸：恰好一种亮才有；都不亮、两种都亮、有读不到的为 null
    /// </summary>
    int? Size { get; }
}
