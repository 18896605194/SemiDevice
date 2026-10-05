using SqlSugar;

namespace xyz.Database.Jobs;

/// <summary>
/// Job 存盘：Job 管理每次发布的全貌（JSON）只留最新一份。
/// 开机读回来：上次没结束的 Job 不接着跑，记成中止进历史；历史也接着留。
/// </summary>
[SugarTable("job_snapshot")]
public class JobSnapshotEntity
{
    /// <summary>只有一行。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public int Id { get; set; } = 1;

    /// <summary>全貌的版本号。</summary>
    public long Version { get; set; }

    /// <summary>全貌（JobListDto 的 JSON）。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Json { get; set; } = string.Empty;

    /// <summary>存的时刻。</summary>
    public DateTime SavedAt { get; set; }
}
