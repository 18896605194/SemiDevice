using SqlSugar;

namespace xyz.Database.Lifetime;

/// <summary>
/// 部件已用次数：一个寿命组件一行
/// </summary>
[SugarTable("component_lifetime")]
public class ComponentLifetimeEntity
{
    /// <summary>
    /// 寿命组件的全路径（如 Chamber.Chamber1.Door.Lifetime）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 256)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 已用次数
    /// </summary>
    public long UsedCount { get; set; }

    /// <summary>
    /// 最后一次改这一行的时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
