using SqlSugar;

namespace xyz.Database.Wafers;

/// <summary>
/// 人工调整晶圆账的记录（设置 → 账单调整页的移动、删除）：谁、什么时候、把哪片从哪挪到哪或删掉、为什么。
/// 人工调整很少，一张表就够，不分表；过期清理跟晶圆流水同一个保留天数。
/// 晶圆流水照常记 Moved / Deleted，这张表只多记操作人和原因，方便事后追查。
/// </summary>
[SugarTable("wafer_adjustment")]
public class WaferAdjustmentEntity
{
    /// <summary>
    /// 主键：雪花号，本身按时间递增。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; } = SnowFlakeSingle.Instance.NextId();

    /// <summary>Move（移动）/ Delete（删除）/ Create（新建，补账）。</summary>
    [SugarColumn(Length = 16)]
    public string Action { get; set; } = string.Empty;

    /// <summary>片的内部唯一标识，跟晶圆流水对得上。</summary>
    [SugarColumn(Length = 36)]
    public string WaferGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string WaferId { get; set; } = string.Empty;

    /// <summary>从哪个位置移走；删除、新建时就是删的、建的那个位置。</summary>
    [SugarColumn(Length = 64)]
    public string FromModule { get; set; } = string.Empty;

    public int FromSlot { get; set; }

    /// <summary>移到哪个模块；删除、新建时为 null。</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? ToModule { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? ToSlot { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? CarrierId { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? LotId { get; set; }

    [SugarColumn(Length = 64)]
    public string Operator { get; set; } = string.Empty;

    [SugarColumn(Length = 256, IsNullable = true)]
    public string? Reason { get; set; }

    public DateTime OccurredAt { get; set; }
}
