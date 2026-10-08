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

    /// <summary>
    /// 载具号：Host 建的是 Host 给的（料可能还没到）；本地建的是来源 LoadPort 上那个载具的（没读到为 null）。EAP 按它找 PJ、报料，料没到时按它认载具。
    /// </summary>
    public string? CarrierId { get; init; }

    /// <summary>
    /// 要做的槽号，按投片顺序（建 PJ 时给的）。Host 没给槽号为空：料到了取载具上全部有片的槽。
    /// 料到之前任务行还没有，报料（E40 的 PRMtlNameList）就报这个。
    /// </summary>
    public IReadOnlyList<int> Slots { get; init; } = [];

    public string? LotId { get; init; }

    public ProcessJobState State { get; internal set; } = ProcessJobState.Created;

    /// <summary>这个 PJ 的片，一片一行任务，按投片顺序。料到了（载具 Load 好、接了 EAP 时槽图也认定了）才定片、生成；在这之前是空的。</summary>
    public List<TaskRow> Rows { get; } = [];

    /// <summary>
    /// 上次定片没成的原因（错误码和参数）：料到了但用不了时记下，同样的原因不重复记日志；定片成了清掉。只由 Job 的扫描线程读写。
    /// </summary>
    internal string? MaterialError { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>开始执行（#4 / #5）的时刻。</summary>
    public DateTime? StartedAt { get; internal set; }

    public DateTime? EndedAt { get; internal set; }

    /// <summary>结束走的转换号（#7 正常、#16 中止、#17 停止、#18 排队时删）；没结束为 null。</summary>
    public int? EndedBy { get; internal set; }

    /// <summary>中止时发给腔体的设备中止动作：都做完了（设备确认了）中止才算做完。</summary>
    public List<ModuleOperation> DeviceAborts { get; } = [];

    /// <summary>库里 process_job 表那一行的 Id（第一次写进去之后才有，0 = 还没写）。只有 Job 管理的存盘线程读写。</summary>
    internal long RowId { get; set; }

    public bool IsEnded => EndedBy is not null;

    /// <summary>料还没到：还没定片（任务行没生成）。定了片的 PJ 至少有一片。</summary>
    public bool IsWaitingForMaterial => Rows.Count == 0;

    /// <summary>还有没投的片。</summary>
    public bool HasWaitingRows => Rows.Any(row => row.IsWaiting);

    /// <summary>有片在机内（投了还没回来）。</summary>
    public bool HasRowsInMachine => Rows.Any(row => row.IsInMachine);

    /// <summary>有在跑的任务（搬运或站内任务）。</summary>
    public bool HasRunning => Rows.Any(row => row.HasRunning);

    /// <summary>有出错等人处理的任务。</summary>
    public bool HasErrors => Rows.Any(row => row.HasError);
}
