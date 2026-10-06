namespace xyz.Modules;

/// <summary>
/// 任务表里的一格：一片要做的一件事（取片、放片、工艺……）。生成时定下任务名、候选站点、配方；
/// 跑的时候记下实际的站点和槽、机械手、出错的原因。只由任务组件在 Job 的扫描线程上改（调度引擎执行时经任务组件改状态）。
/// </summary>
public sealed class WaferTask
{
    /// <summary>任务名（<see cref="StationTaskAction"/>，或站点自己声明的站内任务）。</summary>
    public required string Kind { get; init; }

    /// <summary>
    /// 属于路线的第几站（从 0 开始）：取片、放片是"去第几站"那一趟，站内任务是在第几站做；等于路线站数是回片。
    /// </summary>
    public required int Step { get; init; }

    /// <summary>候选站点：放片、站内任务是这一站能去的站点（站点组，放片时挑一个）；取片为空（从片在的地方取）。</summary>
    public IReadOnlyList<string> Stations { get; init; } = [];

    /// <summary>定好的槽（回片槽）；0 = 派单时挑空槽。</summary>
    public int FixedSlot { get; init; }

    /// <summary>工艺配方名（工艺才有）。</summary>
    public string RecipeName { get; init; } = string.Empty;

    /// <summary>工艺配方快照（工艺才有；没装工艺配方库时为 null，只认名字）。</summary>
    public ProcessRecipeData? Recipe { get; init; }

    public WaferTaskState State { get; internal set; } = WaferTaskState.Waiting;

    /// <summary>实际的站点：取片是从哪取、放片是放到哪、站内任务是在哪做。</summary>
    public string? Station { get; internal set; }

    public int Slot { get; internal set; }

    /// <summary>做这一趟的机械手（取片、放片）。</summary>
    public string? Robot { get; internal set; }

    /// <summary>用的哪只手（取片做完时记下：片在这只手上）。</summary>
    public int Arm { get; internal set; }

    /// <summary>出错的原因（错误码，界面查语言包）；没出错为空。</summary>
    public string Code { get; internal set; } = string.Empty;

    public IReadOnlyList<string> Args { get; internal set; } = [];

    /// <summary>机械手做的任务（取片、放片）；不是的就是站内任务。</summary>
    public bool IsRobotTask => StationTaskAction.IsRobotTask(Kind);

    /// <summary>不用再做了（完成，或未执行）。</summary>
    public bool IsFinished => State is WaferTaskState.Done or WaferTaskState.Cancelled;
}
