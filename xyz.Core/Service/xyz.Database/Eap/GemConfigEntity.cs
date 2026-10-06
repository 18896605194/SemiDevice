using SqlSugar;

namespace xyz.Database.Eap;

/// <summary>
/// GEM（SEMI E30）要掉电保持的设定：Host 定义的报告、事件和报告的链接、关掉的事件和报警、哪些报文要缓存（Spool），
/// 以及缓存的状态（开没开、开始和满了的时刻、累计条数）。整份存成一段 JSON，只有一行。
/// </summary>
[SugarTable("gem_config")]
public class GemConfigEntity
{
    /// <summary>只有一行。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public int Id { get; set; } = 1;

    /// <summary>整份设定（JSON）。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Json { get; set; } = string.Empty;

    /// <summary>存的时刻。</summary>
    public DateTime SavedAt { get; set; }
}
