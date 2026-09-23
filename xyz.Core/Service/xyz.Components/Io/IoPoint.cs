namespace xyz.Components.Io;

/// <summary>
/// 点表里的一行：装机配置，运行期不变。当前值不存在这儿——读的时候直接从 PLC 组件的整块缓存解出来。
/// </summary>
public sealed class IoPoint
{
    /// <summary>PLC 数据块里的数组下标。</summary>
    public int Index { get; init; }

    /// <summary>点名，用于展示和搜索；读写按 Index 定位。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>归属模块（LoadPort1、Chamber1、E84……）。</summary>
    public string Module { get; init; } = string.Empty;

    /// <summary>归属部件（Door、Bowl1、Nozzle_DIW……）。</summary>
    public string Component { get; init; } = string.Empty;

    /// <summary>信号名（CS_0、VALID、Opened……）。</summary>
    public string Tag { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>工程单位（AI/AO 用：mA、V、℃、L/min……）。</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>原始码上下限（AI/AO 标定用，如 0~65535）。</summary>
    public double PhysicalMax { get; init; }

    public double PhysicalMin { get; init; }

    /// <summary>工程量上下限（如 0~10 V）。</summary>
    public double LogicalMax { get; init; }

    public double LogicalMin { get; init; }

    /// <summary>上下限配全了才算标定过；没标定的点直接给原始码。</summary>
    public bool IsScaled =>
        Math.Abs(PhysicalMax - PhysicalMin) > 0.0001 && Math.Abs(LogicalMax - LogicalMin) > 0.0001;

    public double ToEngineering(double raw)
    {
        if (!IsScaled)
        {
            return raw;
        }

        return (raw - PhysicalMin) / (PhysicalMax - PhysicalMin) * (LogicalMax - LogicalMin) + LogicalMin;
    }

    public double ToRaw(double engineering)
    {
        if (!IsScaled)
        {
            return engineering;
        }

        return (engineering - LogicalMin) / (LogicalMax - LogicalMin) * (PhysicalMax - PhysicalMin) + PhysicalMin;
    }
}
