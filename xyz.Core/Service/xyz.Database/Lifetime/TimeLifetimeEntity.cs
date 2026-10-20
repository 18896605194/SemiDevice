using SqlSugar;

namespace xyz.Database.Lifetime;

/// <summary>
/// 部件从哪一刻起算：一个时间寿命组件一行
/// </summary>
[SugarTable("time_lifetime")]
public class TimeLifetimeEntity
{
    /// <summary>
    /// 寿命组件的全路径（如 CDS.IPA.Tank1.Lifetime）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 256)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 从这一刻起算（上次清零的时间）
    /// </summary>
    public DateTime StartedAt { get; set; }
}
