namespace xyz.Modules;

/// <summary>
/// 调度这一拍算出来的计划：要起的工艺、要下的搬运单、没排上的片在等什么。JobManager 照着去提交，提交不上的也记成等待。
/// </summary>
public sealed class JobPlan
{
    public List<PlannedProcess> Processes { get; } = [];

    public List<PlannedTransfer> Transfers { get; } = [];

    /// <summary>没排上的片（晶圆账内部标识）→ 在等什么。</summary>
    public Dictionary<Guid, JobWait> Waits { get; } = [];
}

/// <summary>
/// 计划起一次工艺：这一片在它到了的站点上做路线上这一步的配方。
/// </summary>
public sealed record PlannedProcess(ProcessJob Job, JobWafer Wafer, string Station, int Slot, JobRouteStep Step);

/// <summary>
/// 计划下一张搬运单：把这一片从源搬到目标，目标是路线上的第 TargetStep 步（= 步数表示回片）。
/// </summary>
public sealed record PlannedTransfer(
    ProcessJob Job,
    JobWafer Wafer,
    string Source,
    int SourceSlot,
    string Target,
    int TargetSlot,
    int TargetStep,
    string Robot);
