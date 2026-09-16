using SqlSugar;

namespace xyz.Database.Wafers;

/// <summary>
/// 晶圆流水：账本每变动一次写一行（建片、移片、改信息、删片），用来回溯一片走过哪些位置。
/// 当前状态看账本本身，这张表只管历史。
/// 按天分表：实际表名是 wafer_history_20260916 这种，按 OccurredAt 分流；
/// 过期清理直接删整张表，不用逐行删，单表也不会越写越大。
/// </summary>
[SugarTable("wafer_history_{year}{month}{day}")]
[SplitTable(SplitType.Day)]
public class WaferHistoryEntity
{
    /// <summary>
    /// 主键：雪花号（分表不能用自增列），本身按时间递增，排序即时间序。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; } = SnowFlakeSingle.Instance.NextId();

    /// <summary>变动类型：Created / Moved / Updated / Deleted。</summary>
    [SugarColumn(Length = 16)]
    public string Action { get; set; } = string.Empty;

    /// <summary>片的内部唯一标识，跨位置不变，用它串起一片的全程。</summary>
    [SugarColumn(Length = 36)]
    public string WaferGuid { get; set; } = string.Empty;

    /// <summary>业务片号（读码或 Host 改写后会变，所以每行都记当时的值）。</summary>
    [SugarColumn(Length = 64)]
    public string WaferId { get; set; } = string.Empty;

    /// <summary>变动后所在模块。</summary>
    [SugarColumn(Length = 64)]
    public string Module { get; set; } = string.Empty;

    /// <summary>变动后所在槽位。</summary>
    public int Slot { get; set; }

    /// <summary>移片时的原模块；其它变动为 null。</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? FromModule { get; set; }

    /// <summary>移片时的原槽位；其它变动为 null。</summary>
    [SugarColumn(IsNullable = true)]
    public int? FromSlot { get; set; }

    /// <summary>所属载具。</summary>
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

    /// <summary>变动发生的时刻（账本上的时间，不是落库时间）；也是分表依据。</summary>
    [SplitField]
    public DateTime OccurredAt { get; set; }
}
