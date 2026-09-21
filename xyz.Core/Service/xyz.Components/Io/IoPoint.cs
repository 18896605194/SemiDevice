namespace xyz.Components.Io;

/// <summary>
/// 一个 IO 点：点表里的装机信息（索引、名字、归属、标定）+ 每拍刷新的当前值。
/// 点表由电控给，跟仿真器共用同一份 csv。
/// </summary>
public sealed class IoPoint
{
    #region 点表（装机配置，运行期不变）

    /// <summary>PLC 数据块里的数组下标。</summary>
    public int Index { get; init; }

    /// <summary>点名，全表唯一；业务组件按它取点。</summary>
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

    #endregion

    #region 当前值（采集线程每拍刷，读的人可能拿到上一拍的，50ms 级无所谓）

    /// <summary>这一拍有没有读到。读不到时下面两个值不可信——别拿陈旧值当真。</summary>
    public bool IsValid { get; internal set; }

    /// <summary>数字量当前状态（DI/DO）。</summary>
    public bool IsOn { get; internal set; }

    /// <summary>模拟量当前原始码（AI/AO）。</summary>
    public double Raw { get; internal set; }

    /// <summary>模拟量当前工程值；没标定就等于原始码。</summary>
    public double Value => IsScaled ? ToEngineering(Raw) : Raw;

    #endregion

    /// <summary>原始码 → 工程值（线性标定，跟仿真器同一个公式）。</summary>
    public double ToEngineering(double raw)
    {
        if (!IsScaled)
        {
            return raw;
        }

        return (raw - PhysicalMin) / (PhysicalMax - PhysicalMin) * (LogicalMax - LogicalMin) + LogicalMin;
    }

    /// <summary>工程值 → 原始码（写 AO 用）。</summary>
    public double ToRaw(double engineering)
    {
        if (!IsScaled)
        {
            return engineering;
        }

        return (engineering - LogicalMin) / (LogicalMax - LogicalMin) * (PhysicalMax - PhysicalMin) + PhysicalMin;
    }
}
