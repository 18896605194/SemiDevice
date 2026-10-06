using xyz.Components.Components;
using xyz.Components.Enums;

namespace xyz.Modules;


public sealed class ProcessJob
{
    /// <summary>PRJobID（E39 的 ObjID：ASCII，不能有 ? * ~ &gt; :）。</summary>
    public required string Id { get; init; }

    /// <summary>所属 CJ；还没被 CJ 收进去为 null。</summary>
    public ControlJob? ControlJob { get; set; }

    /// <summary>流程配方快照（建 PJ 时从库里取的副本：之后库里改名、改内容、删掉都不影响这个 PJ）。</summary>
    public required SequenceData Sequence { get; init; }

    /// <summary>载具号：建 PJ 时来源 LoadPort 上那个载具的（没读到为 null）。EAP 按它找 PJ、报料。</summary>
    public string? CarrierId { get; init; }

    /// <summary>建 PJ 时 LoadPort 上那个载具对象的标识：CJ 跟着记，载具拿走（或换了一个）之后完成的 CJ 才删（#13）。</summary>
    public Guid? CarrierInstance { get; init; }

    /// <summary>PRProcessStart：准备好了直接开始（true），还是等 Start 命令（false）。</summary>
    public bool AutoStart { get; init; } = true;

    public PrJobState State { get; internal set; } = PrJobState.QueuedPooled;

    /// <summary>暂停前的执行子状态：恢复（#10）回到这里。</summary>
    public PrJobState ResumeState { get; internal set; } = PrJobState.Processing;

    /// <summary>这个 PJ 的片，一片一行任务，按投片顺序（取片顺序在建 PJ 时排好）。</summary>
    public List<TaskRow> Rows { get; } = [];

    /// <summary>谁建的。</summary>
    public JobCommandSource CreatedBy { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>开始执行（#4 / #5）的时刻。</summary>
    public DateTime? StartedAt { get; internal set; }

    public DateTime? EndedAt { get; internal set; }

    /// <summary>结束走的转换号（#7 正常、#16 中止、#17 停止、#18 排队时删）；没结束为 null。</summary>
    public int? EndedBy { get; internal set; }

    /// <summary>中止时发给腔体的设备中止动作：都做完了（设备确认了）中止才算做完。</summary>
    public List<ModuleOperation> DeviceAborts { get; } = [];

    public bool IsEnded => EndedBy is not null;

    /// <summary>还有没投的片。</summary>
    public bool HasWaitingRows => Rows.Any(row => row.IsWaiting);

    /// <summary>有片在机内（投了还没回来）。</summary>
    public bool HasRowsInMachine => Rows.Any(row => row.IsInMachine);

    /// <summary>有在跑的任务（搬运或站内任务）。</summary>
    public bool HasRunning => Rows.Any(row => row.HasRunning);

    /// <summary>有出错等人处理的任务。</summary>
    public bool HasErrors => Rows.Any(row => row.HasError);
}
