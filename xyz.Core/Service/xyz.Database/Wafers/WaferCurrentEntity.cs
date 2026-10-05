using SqlSugar;

namespace xyz.Database.Wafers;

/// <summary>
/// 晶圆账的存盘（当前账）：账上现在的每一片一行。账一变，写库线程隔一小会儿把整张表重写一遍；
/// 开机按它把腔体、机械手这些位置上的片放回去（LoadPort 上的不恢复，以开机 Mapping 为准）。
/// 跟晶圆流水不同：流水是每次变动一行、按天分表，这张只留"现在"。
/// </summary>
[SugarTable("wafer_current")]
public class WaferCurrentEntity
{
    /// <summary>片的内部唯一标识（恢复后还是同一个，跟流水、调整记录对得上）。</summary>
    [SugarColumn(IsPrimaryKey = true, Length = 36)]
    public string WaferGuid { get; set; } = string.Empty;

    /// <summary>业务片号。</summary>
    [SugarColumn(Length = 64)]
    public string WaferId { get; set; } = string.Empty;

    /// <summary>现在所在模块（机械手的手臂也算模块）。</summary>
    [SugarColumn(Length = 64)]
    public string Module { get; set; } = string.Empty;

    /// <summary>现在所在槽位（手臂号即槽位号）。</summary>
    public int Slot { get; set; }

    /// <summary>建片时所在模块。</summary>
    [SugarColumn(Length = 64)]
    public string OriginModule { get; set; } = string.Empty;

    /// <summary>建片时所在槽位。</summary>
    public int OriginSlot { get; set; }

    /// <summary>来源 LoadPort（回片回到这儿）；不是在 LoadPort 上建的片为 null。</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SourceLoadPort { get; set; }

    /// <summary>来源 LoadPort 的槽号；不是在 LoadPort 上建的片为 0。</summary>
    public int SourceSlot { get; set; }

    /// <summary>建片时所在载具。</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? OriginCarrierId { get; set; }

    /// <summary>当前所属载具。</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? CarrierId { get; set; }

    /// <summary>批次号。</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? LotId { get; set; }

    /// <summary>物理状态（正常/交叉/叠片/陪片）。</summary>
    [SugarColumn(Length = 16)]
    public string Status { get; set; } = string.Empty;

    /// <summary>工艺状态。</summary>
    [SugarColumn(Length = 16)]
    public string ProcessState { get; set; } = string.Empty;

    /// <summary>建片时刻。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最后一次变动时刻。</summary>
    public DateTime UpdatedAt { get; set; }
}
