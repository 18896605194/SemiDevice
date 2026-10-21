using SqlSugar;

namespace xyz.Database.Lifetime;

/// <summary>
/// 部件已用时间：一个时间寿命组件一行
/// </summary>
[SugarTable("time_lifetime")]
public class TimeLifetimeEntity
{
    /// <summary>
    /// 寿命组件的全路径（如 CDS.IPA.Tank1Chemical）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 256)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 上次清零的时间（不盯 DO 的从这一刻起算）
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// 盯计时 DO 的：DO 通着累计的秒数（默认值 0 不能删：老表补这一列时靠它把老行填 0，空值往上加还是空）
    /// </summary>
    [SugarColumn(DefaultValue = "0")]
    public long UsedSeconds { get; set; }
}
