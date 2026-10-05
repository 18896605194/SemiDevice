using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// Job 全貌（推送，留存；token = <see cref="EventToken"/>）：没结束的 CJ、PJ（状态值照 SEMI E94 / E40），最近结束的 CJ，
/// 以及自动派单是不是因为出错暂停了。版本号每变一次加 1，客户端重连后拿到的就是最新的一份。
/// </summary>
public class JobListDto
{
    public const string EventToken = "Job";

    /// <summary>版本：内容每变一次加 1。</summary>
    public long Version { get; set; }

    /// <summary>没删的 CJ，按队列顺序（在跑的在前、排队的按队列先后）。</summary>
    public List<ControlJobDto> ControlJobs { get; set; } = [];

    /// <summary>没结束的 PJ（含还不归任何 CJ 的），按建的先后。</summary>
    public List<ProcessJobDto> ProcessJobs { get; set; } = [];

    /// <summary>最近删掉的 CJ（结束后转历史），新的在前。</summary>
    public List<ControlJobDto> History { get; set; } = [];

    /// <summary>出过执行故障，自动派单暂停了：人工确认后恢复。</summary>
    public bool IsHeld { get; set; }

    /// <summary>为什么暂停派单（错误码，界面查语言包）。</summary>
    public string HoldCode { get; set; } = string.Empty;

    public List<string> HoldArgs { get; set; } = [];
}

/// <summary>
/// 一个 CJ（SEMI E94 Control Job）。
/// </summary>
public class ControlJobDto
{
    public string Id { get; set; } = string.Empty;

    public string LoadPort { get; set; } = string.Empty;

    public string CarrierId { get; set; } = string.Empty;

    public string LotId { get; set; } = string.Empty;

    /// <summary>E94 状态值：0 QUEUED、1 SELECTED、2 WAITINGFORSTART、3 EXECUTING、4 PAUSED、5 COMPLETED。</summary>
    public int State { get; set; }

    /// <summary>StartMethod：料到了直接开始。</summary>
    public bool AutoStart { get; set; }

    /// <summary>收下的 Stop / Abort，等 PJ 都结束：None / Stop / Abort。</summary>
    public string Ending { get; set; } = "None";

    /// <summary>下面的 PJ，按执行顺序。</summary>
    public List<string> ProcessJobs { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>进 COMPLETED 走的转换号：10 正常、11 停止、12 中止；没完成为 0。</summary>
    public int CompletedBy { get; set; }

    /// <summary>删掉走的转换号：2 排队时删、13 完成后删；没删为 0。</summary>
    public int EndedBy { get; set; }

    public DateTime? EndedAt { get; set; }

    public bool NeedsRecovery { get; set; }
}

/// <summary>
/// 一个 PJ（SEMI E40 Process Job）。
/// </summary>
public class ProcessJobDto
{
    public string Id { get; set; } = string.Empty;

    /// <summary>所属 CJ；还不归任何 CJ 为空。</summary>
    public string ControlJob { get; set; } = string.Empty;

    /// <summary>流程配方名（快照的）。</summary>
    public string Sequence { get; set; } = string.Empty;

    public int SequenceRevision { get; set; }

    /// <summary>路线上中间要经过几站。</summary>
    public int StepCount { get; set; }

    /// <summary>
    /// E40 状态值：0 QUEUED/POOLED、1 SETTING UP、2 WAITING FOR START、3 PROCESSING、4 PROCESS COMPLETE、
    /// 6 PAUSING、7 PAUSED、8 STOPPING、9 ABORTING、10 STOPPED、11 ABORTED。
    /// </summary>
    public int State { get; set; }

    /// <summary>PRProcessStart：准备好了直接开始。</summary>
    public bool AutoStart { get; set; }

    public List<JobWaferDto> Wafers { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    /// <summary>结束走的转换号：7 正常、16 中止、17 停止、18 排队时删；没结束为 0。</summary>
    public int EndedBy { get; set; }

    public bool NeedsRecovery { get; set; }
}

/// <summary>
/// PJ 里的一片：从哪来回哪去、走到路线第几步、在哪、在等什么、每一站的结果。
/// </summary>
public class JobWaferDto
{
    public string WaferId { get; set; } = string.Empty;

    public string SourcePort { get; set; } = string.Empty;

    public int SourceSlot { get; set; }

    public string ReturnPort { get; set; } = string.Empty;

    public int ReturnSlot { get; set; }

    /// <summary>路线上第几步：-1 还在来源槽；0 起是中间那一站；等于 PJ 的 StepCount 表示回片。</summary>
    public int Step { get; set; }

    /// <summary>Waiting / Moving / Arrived / Processing / Processed / Done / Lost。</summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>None / Completed / Failed / NotRun / Aborted。</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>到站后所在的站点（在途时是要去的站点）。</summary>
    public string Station { get; set; } = string.Empty;

    public int Slot { get; set; }

    /// <summary>在等什么（错误码，界面查语言包）；不在等为空。</summary>
    public string WaitCode { get; set; } = string.Empty;

    public List<string> WaitArgs { get; set; } = [];

    public List<JobStepResultDto> Results { get; set; } = [];
}

/// <summary>
/// 一片在一站的加工结果。
/// </summary>
public class JobStepResultDto
{
    public int Step { get; set; }

    public string Station { get; set; } = string.Empty;

    public string Recipe { get; set; } = string.Empty;

    public int RecipeRevision { get; set; }

    public bool Success { get; set; }

    public string Code { get; set; } = string.Empty;

    public List<string> Args { get; set; } = [];

    /// <summary>模拟加工（设备驱动没接，计时就算做完）。</summary>
    public bool Simulated { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime EndedAt { get; set; }
}

/// <summary>
/// 本地建 Job：一个 LoadPort 上的一篮，每槽一个流程配方（相同流程配方的槽分成一个 PJ），整篮一个 CJ。
/// </summary>
[ProtoContract]
public class JobCreateRequest
{
    [ProtoMember(1)]
    public string LoadPort { get; set; } = string.Empty;

    /// <summary>批次号，也是 CJ 的名字；空就自动起名。</summary>
    [ProtoMember(2)]
    public string LotId { get; set; } = string.Empty;

    /// <summary>要做的槽和各自的流程配方。</summary>
    [ProtoMember(3)]
    public List<JobSlotDto> Slots { get; set; } = [];

    /// <summary>建好、料到了直接开始；false = 等"启动 Job"。</summary>
    [ProtoMember(4)]
    public bool AutoStart { get; set; }

    /// <summary>请求号：同一个请求重发回同一个结果，不会建两份。</summary>
    [ProtoMember(5)]
    public string RequestId { get; set; } = string.Empty;

    [ProtoMember(6)]
    public string Operator { get; set; } = string.Empty;
}

/// <summary>
/// 一槽和它的流程配方。
/// </summary>
[ProtoContract]
public class JobSlotDto
{
    [ProtoMember(1)]
    public int Slot { get; set; }

    [ProtoMember(2)]
    public string Sequence { get; set; } = string.Empty;
}

/// <summary>
/// CJ / PJ 命令：CJ 的命令值照 E94（1 Start、2 Pause、3 Resume、4 Cancel、5 Deselect、6 Stop、7 Abort、8 HOQ），
/// Action 照 E94（0 SaveJobs、1 RemoveJobs）；PJ 的命令值 0 Start、1 Pause、2 Resume、3 Stop、4 Abort、5 Cancel。
/// </summary>
[ProtoContract]
public class JobCommandRequest
{
    [ProtoMember(1)]
    public string JobId { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int Command { get; set; }

    [ProtoMember(3)]
    public int Action { get; set; }

    /// <summary>请求号：同一个请求重发回同一个结果。</summary>
    [ProtoMember(4)]
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// 建好的 Job：CJ 名和下面的 PJ 名（走 JSON）。
/// </summary>
public class JobCreatedDto
{
    public string ControlJob { get; set; } = string.Empty;

    public List<string> ProcessJobs { get; set; } = [];
}
