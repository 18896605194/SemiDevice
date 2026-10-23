using System.Text.Json.Serialization;
using xyz.Components.Components;

namespace xyz.Modules;

/// <summary>
/// 任务表里的一格：一片要做的一件事（取片、放片、工艺……）。生成时定下任务名、候选站点、配方；
/// 跑的时候记下实际的站点和槽、机械手、出错的原因。只由任务组件在 Job 的扫描线程上改（调度引擎执行时经任务组件改状态）。
/// </summary>
public sealed class WaferTask
{
    /// <summary>
    /// 任务名
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// 属于路线的第几站
    /// </summary>
    public required int Step { get; init; }

    /// <summary>
    /// 放片、站内任务是这一站能去的站点
    /// </summary>
    public IReadOnlyList<string> Stations { get; init; } = [];

    /// <summary>
    /// 回片槽
    /// </summary>
    public int FixedSlot { get; init; }

    /// <summary>
    /// 工艺配方名
    /// </summary>
    public string RecipeName { get; init; } = string.Empty;

    /// <summary>
    /// <summary>（工艺才有；没装工艺配方库时为 null，只认名字）。</summary> 
    /// </summary>
    public RecipeData? Recipe { get; init; }

    public WaferTaskState State { get; internal set; } = WaferTaskState.Waiting;

    /// <summary>
    /// 当前在做的动作；调度直接从这一格收进度，不另建执行任务表。
    /// </summary>
    [JsonIgnore]
    public ModuleRoutine? Routine { get; internal set; }

    /// <summary>
    /// 实际的站点：取片是从哪取、放片是放到哪、站内任务是在哪做。
    /// </summary>
    public string? Station { get; internal set; }

    public int Slot { get; internal set; }

    /// <summary>
    /// 做这一趟的机械手
    /// </summary>
    public string? Robot { get; internal set; }

    /// <summary>
    /// 用的哪只手s
    /// </summary>
    public int Arm { get; internal set; }

    /// <summary>
    /// 出错的原因
    /// </summary>
    public string Code { get; internal set; } = string.Empty;

    public IReadOnlyList<string> Args { get; internal set; } = [];

    /// <summary>机械手做的任务（取片、放片）；不是的就是站内任务。</summary>
    public bool IsRobotTask => StationTaskAction.IsRobotTask(Kind);

    /// <summary>不用再做了（完成，或未执行）。</summary>
    public bool IsFinished => State is WaferTaskState.Done or WaferTaskState.Cancelled;
}
