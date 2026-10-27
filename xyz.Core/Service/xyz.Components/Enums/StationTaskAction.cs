namespace xyz.Components.Enums;

/// <summary>
/// 框架提供的基础任务
/// </summary>
public static class StationTaskAction
{
    public const string Pick = "Pick";
    public const string Place = "Place";

    public const string Process = "Process";

    /// <summary>只能取放、没有站内任务的站点（LoadPort 这类）支持的。</summary>
    public static IReadOnlyList<string> PickPlace { get; } = [Pick, Place];

    /// <summary>
    /// 机械手做的任务
    /// </summary>
    /// <param name="kind"></param>
    /// <returns></returns>
    public static bool IsRobotTask(string kind)
    {
        return kind == Pick || kind == Place;
    }
}
