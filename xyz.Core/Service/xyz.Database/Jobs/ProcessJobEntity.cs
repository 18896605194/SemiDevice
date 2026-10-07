using SqlSugar;

namespace xyz.Database.Jobs;

/// <summary>
/// PJ 记录（SEMI E40 Process Job）：一个 PJ 一行，建好就插、之后每次变化更新这一行，结束那次写最后一遍；
/// 每片的任务明细（JobWaferDto 列表）整个放在 <see cref="Wafers"/> 里。
/// 开机时还没结束的（EndedBy = 0）就是重启前没做完的：记成中止。
/// </summary>
[SugarTable("process_job")]
public class ProcessJobEntity : BaseEntity
{
    /// <summary>PJ 名（PRJobID）。</summary>
    [SugarColumn(Length = 80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>所属 CJ 的名字；还不归任何 CJ 为空。</summary>
    [SugarColumn(Length = 80)]
    public string ControlJob { get; set; } = string.Empty;

    /// <summary>所属 CJ 在 control_job 表里那一行的 Id（CJ 名以后可能重复，按它找）；不归 CJ 为 0。</summary>
    public long ControlJobRowId { get; set; }

    [SugarColumn(Length = 80)]
    public string CarrierId { get; set; } = string.Empty;

    /// <summary>流程配方名（快照的）。</summary>
    [SugarColumn(Length = 128)]
    public string Sequence { get; set; } = string.Empty;

    public int SequenceRevision { get; set; }

    /// <summary>
    /// E40 状态值：0 QUEUED/POOLED、1 SETTING UP、2 WAITING FOR START、3 PROCESSING、4 PROCESS COMPLETE、
    /// 6 PAUSING、7 PAUSED、8 STOPPING、9 ABORTING、10 STOPPED、11 ABORTED。
    /// </summary>
    public int State { get; set; }

    /// <summary>PRProcessStart：准备好了直接开始。</summary>
    public bool AutoStart { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? StartedAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? EndedAt { get; set; }

    /// <summary>结束走的转换号：7 正常、16 中止、17 停止、18 排队时删；没结束为 0。</summary>
    public int EndedBy { get; set; }

    /// <summary>每片和它的一行任务（JobWaferDto 列表的 JSON）。</summary>
    [SugarColumn(ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Wafers { get; set; } = string.Empty;
}
