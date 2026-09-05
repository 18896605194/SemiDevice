using SqlSugar;

namespace xyz.Database;

/// <summary>
/// 数据模型基类，统一主键、启用状态、创建/更新时间。
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// 主键，自增
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnDataType = "INTEGER")]
    public long Id { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 更新时间
    /// </summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedTime { get; set; }
}
