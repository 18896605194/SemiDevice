namespace xyz.Modules;

/// <summary>
/// 框架提供的基础任务（Job 任务表里一格就是一个任务）。取片、放片归机械手，经搬运管理做；其余都是站内任务，交给站点自己执行。
/// 用名字区分：以后新站点要新的站内任务（比如对准），站点自己声明一个名字、自己实现 StartTask，调度不用改。
/// </summary>
public static class StationTaskAction
{
    /// <summary>取片：机械手把片从站点上取走。</summary>
    public const string Pick = "Pick";

    /// <summary>放片：机械手把片放进站点。</summary>
    public const string Place = "Place";

    /// <summary>工艺：腔体这类站点按这一步的工艺配方加工。</summary>
    public const string Process = "Process";

    /// <summary>只能取放、没有站内任务的站点（LoadPort 这类）支持的。</summary>
    public static IReadOnlyList<string> PickPlace { get; } = [Pick, Place];

    /// <summary>是机械手做的任务（取片、放片）；不是的就是站内任务。</summary>
    public static bool IsRobotTask(string kind)
    {
        return kind == Pick || kind == Place;
    }
}

/// <summary>
/// 起一个站内任务的请求：做哪种、给哪个 PJ 的哪一片、在站点的哪一槽、路线上第几步、用哪个工艺配方（工艺才有）。
/// </summary>
public sealed record StationTaskRequest
{
    /// <summary>任务名（<see cref="StationTaskAction"/> 里的，或站点自己声明的）。</summary>
    public required string Kind { get; init; }

    /// <summary>起任务的 PJ 名。</summary>
    public string? Owner { get; init; }

    /// <summary>要做的那一片（晶圆账内部标识）：站点上不是这一片就不起。</summary>
    public Guid? WaferId { get; init; }

    /// <summary>站点上的槽号（一般站点只有 1 槽）。</summary>
    public int Slot { get; init; } = 1;

    /// <summary>路线上第几步（从 0 开始）。</summary>
    public int Step { get; init; } = -1;

    /// <summary>工艺配方名；不是工艺为空。</summary>
    public string RecipeName { get; init; } = string.Empty;

    /// <summary>工艺配方快照；没装工艺配方库时为 null（只认名字）。</summary>
    public ProcessRecipeData? Recipe { get; init; }
}
