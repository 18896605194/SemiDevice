using SqlSugar;

namespace xyz.Database.Eap;

/// <summary>
/// GEM 缓存（Spool）的一条：跟 Host 断了通讯时，Host 要求缓存的报文（事件报告、报警）先存在这里，
/// 等 Host 用 S6F23 要了再按先后发出去（或者 Host 让清掉）。存盘是 E30 的要求：断电重启也不能丢。
/// </summary>
[SugarTable("gem_spool")]
public class GemSpoolEntity
{
    /// <summary>主键：雪花号，按时间递增，排序就是先后。</summary>
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; } = SnowFlakeSingle.Instance.NextId();

    public byte Stream { get; set; }

    public byte Function { get; set; }

    /// <summary>W 位：发出去要不要等回复。</summary>
    public bool ReplyExpected { get; set; }

    /// <summary>报文体（SECS-II 编码后的字节）；没有体为空。</summary>
    [SugarColumn(IsNullable = true)]
    public byte[]? Body { get; set; }

    /// <summary>进缓存的时刻。</summary>
    public DateTime CreatedAt { get; set; }
}
