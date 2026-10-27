namespace xyz.Elements.Models;

/// <summary>
/// 导出的一段原始数据：这段时间里库里的每一行（UTC 毫秒），每条曲线在这些时刻的值（没读到为 null）。
/// Values[i] 是 Names[i] 那条，跟 Times 一一对应。
/// </summary>
public sealed class DataExport
{
    public required long[] Times { get; init; }

    public required IReadOnlyList<string> Names { get; init; }

    public required double?[][] Values { get; init; }
}
