namespace xyz.Modules;

/// <summary>
/// 调度看得到的设备视图（JobManager 每拍刷新一份交给调度）：站点能不能派片、槽空不空、哪台机械手能接、站点能不能起工艺。
/// 调度只看它做计算，不碰设备、不改 Job：换调度策略（sc.xml 换 Scheduler 的 Type）也不用管设备怎么查。
/// </summary>
public interface IJobPlanEnvironment
{
    /// <summary>
    /// 这个站点现在能不能服务机械手：装了、启用、在待命（可服务）、没在做别的动作；
    /// 腔体这类可选的站点还要在线（模块 Online，表示参与自动调度），LoadPort 不看在线。
    /// </summary>
    bool IsStationReady(string station);

    /// <summary>站点槽数；不认识的站点为 0。</summary>
    int SlotCount(string station);

    /// <summary>这一槽账上空着、也没被搬运单锁着。</summary>
    bool IsSlotFree(string station, int slot);

    /// <summary>
    /// 哪台机械手现在能接这一趟（两个站点都在它的站点表里、它手上没单、空闲、有一只空着且两边都许用的手）；
    /// exclude 里的不算（这一拍已经派了单的）。没有返回 null。
    /// </summary>
    string? RobotFor(string source, string target, IReadOnlySet<string> exclude);

    /// <summary>站点能不能加工（腔体这类）。</summary>
    bool IsProcessStation(string station);

    /// <summary>这一站现在能不能起这个工艺（不动设备）：能起返回 null。</summary>
    ProcessRejection? CheckProcess(string station, ProcessRequest request);
}
