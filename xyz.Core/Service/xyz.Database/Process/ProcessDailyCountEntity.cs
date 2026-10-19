using SqlSugar;

namespace xyz.Database.Process;

/// <summary>
/// 工艺站点每天开工了几片：一个站一天一行
/// </summary>
[SugarTable("process_daily_count")]
public class ProcessDailyCountEntity
{
    /// <summary>
    /// 站点名（模块名，如 Chamber1）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string Station { get; set; } = string.Empty;

    /// <summary>
    /// 哪一天（本机日期，yyyy-MM-dd）
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, Length = 10)]
    public string Day { get; set; } = string.Empty;

    /// <summary>
    /// 这一天开工了几片（中止、失败的也算）
    /// </summary>
    public int Wafers { get; set; }

    /// <summary>
    /// 最后一次改这一行的时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
