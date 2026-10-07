using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// Job 全貌（推送，留存；token = <see cref="EventToken"/>）：没删的 CJ、界面要看的 PJ（状态值照 SEMI E94 / E40，每片带它的一行任务）。
/// 客户端重连后拿到当前快照。删掉的 CJ、结束的 PJ 在库里（control_job、process_job 两张表）。
/// </summary>
public class JobListDto
{
    public const string EventToken = "Job";

    /// <summary>没删的 CJ，按队列顺序（在跑的在前、排队的按队列先后）。</summary>
    public List<ControlJobDto> ControlJobs { get; set; } = [];

    /// <summary>没结束的 PJ（含还不归任何 CJ 的），按建的先后。</summary>
    public List<ProcessJobDto> ProcessJobs { get; set; } = [];
}

/// <summary>
/// 一个 CJ（SEMI E94 Control Job）。
/// </summary>
public class ControlJobDto
{
    /// <summary>E94 状态值 WAITINGFORSTART：料到了，等启动命令。</summary>
    public const int StateWaitingForStart = 2;

    public string Id { get; set; } = string.Empty;

    public string LoadPort { get; set; } = string.Empty;

    public string CarrierId { get; set; } = string.Empty;

    public string LotId { get; set; } = string.Empty;

    /// <summary>E94 状态值：0 QUEUED、1 SELECTED、2 WAITINGFORSTART、3 EXECUTING、4 PAUSED、5 COMPLETED。</summary>
    public int State { get; set; }

    /// <summary>StartMethod：设备 SC 的 CJ 自动启动配置值，用于查询和上报。</summary>
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
}

/// <summary>
/// 一个 PJ（SEMI E40 Process Job）。
/// </summary>
public class ProcessJobDto
{
    public string Id { get; set; } = string.Empty;

    public string LotId { get; set; } = string.Empty;

    /// <summary>所属 CJ；还不归任何 CJ 为空。</summary>
    public string ControlJob { get; set; } = string.Empty;

    /// <summary>载具号（建 PJ 时 LoadPort 上那个载具的；没读到为空）。</summary>
    public string CarrierId { get; set; } = string.Empty;

    /// <summary>流程配方名（快照的）。</summary>
    public string Sequence { get; set; } = string.Empty;

    public int SequenceRevision { get; set; }

    /// <summary>
    /// E40 状态值：0 QUEUED/POOLED、1 SETTING UP、2 WAITING FOR START、3 PROCESSING、4 PROCESS COMPLETE、
    /// 6 PAUSING、7 PAUSED、8 STOPPING、9 ABORTING、10 STOPPED、11 ABORTED。
    /// </summary>
    public int State { get; set; }

    /// <summary>PRProcessStart：设备 SC 的 PJ 自动启动配置值，用于查询和上报。</summary>
    public bool AutoStart { get; set; }

    public List<JobWaferDto> Wafers { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    /// <summary>结束走的转换号：7 正常、16 中止、17 停止、18 排队时删；没结束为 0。</summary>
    public int EndedBy { get; set; }
}

/// <summary>
/// PJ 里的一片：从哪来回哪去，以及它的一行任务（取片、放片、工艺……按顺序走）。做没做成看晶圆账（片的工艺状态）。
/// </summary>
public class JobWaferDto
{
    public string WaferId { get; set; } = string.Empty;

    public string SourcePort { get; set; } = string.Empty;

    public int SourceSlot { get; set; }

    public string ReturnPort { get; set; } = string.Empty;

    public int ReturnSlot { get; set; }

    /// <summary>这一片的任务，按顺序。</summary>
    public List<JobTaskDto> Tasks { get; set; } = [];
}

/// <summary>
/// 一片的一个任务（任务表里的一格）。
/// </summary>
public class JobTaskDto
{
    /// <summary>Pick / Place / Process（站点自己声明的站内任务也可能是别的名字）。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>属于路线的第几站（从 0 开始）；最大的那个是回片那一趟。</summary>
    public int Step { get; set; }

    /// <summary>候选站点（站点组）；取片为空。</summary>
    public List<string> Stations { get; set; } = [];

    /// <summary>实际的站点：取片从哪取、放片放到哪、站内任务在哪做；还没定为空。</summary>
    public string Station { get; set; } = string.Empty;

    public int Slot { get; set; }

    /// <summary>工艺配方名（工艺才有）。</summary>
    public string Recipe { get; set; } = string.Empty;

    public int RecipeRevision { get; set; }

    /// <summary>Waiting / Running / Done / Error / Cancelled。</summary>
    public string State { get; set; } = string.Empty;

    public string Robot { get; set; } = string.Empty;

    public int Arm { get; set; }

    /// <summary>出错的原因（错误码，界面查语言包）；没出错为空。</summary>
    public string Code { get; set; } = string.Empty;

    public List<string> Args { get; set; } = [];
}

/// <summary>
/// 创建独立 PJ，建好后等待 CJ 关联。
/// </summary>
[ProtoContract]
public class ProcessJobCreateRequest
{
    [ProtoMember(1)] public string LoadPort { get; set; } = string.Empty;
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public List<int> Slots { get; set; } = [];
    [ProtoMember(4)] public string Sequence { get; set; } = string.Empty;
    [ProtoMember(5)] public string LotId { get; set; } = string.Empty;
}

/// <summary>关联已经创建的 PJ；不会按槽位或流程配方重新创建 PJ。Name 为空时用 LotId 或自动生成 CJ 名。</summary>
[ProtoContract]
public class ControlJobCreateRequest
{
    [ProtoMember(1)] public string LoadPort { get; set; } = string.Empty;
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public List<string> ProcessJobs { get; set; } = [];
    [ProtoMember(5)] public string LotId { get; set; } = string.Empty;
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
    /// <summary>CJ 命令值 Start（E94 CTLJOBCMD 1）。</summary>
    public const int ControlJobStart = 1;

    [ProtoMember(1)]
    public string JobId { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int Command { get; set; }

    [ProtoMember(3)]
    public int Action { get; set; }
}

/// <summary>
/// 出错任务的人工处理（重做 / 标记完成）：哪个 PJ、哪一片（来源槽号）、这一片的第几个任务（从 0 开始）。只给本地界面，Host 不碰。
/// </summary>
[ProtoContract]
public class JobTaskRequest
{
    [ProtoMember(1)]
    public string ProcessJob { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int Slot { get; set; }

    [ProtoMember(3)]
    public int Task { get; set; }
}

/// <summary>
/// 建好的 Job：CJ 名和下面的 PJ 名（走 JSON）。
/// </summary>
public class JobCreatedDto
{
    public string ControlJob { get; set; } = string.Empty;

    public List<string> ProcessJobs { get; set; } = [];
}
