using xyz.Common.Log;

namespace xyz.Components.Collectors;

/// <summary>
/// 一键采集的结果：五类一次取全。
/// </summary>
public sealed class GemSnapshot
{
    public IReadOnlyList<CollectedEc> Ecs { get; init; } = [];

    public IReadOnlyList<CollectedSv> Svs { get; init; } = [];

    public IReadOnlyList<CollectedAlarm> Alarms { get; init; } = [];

    public IReadOnlyList<CollectedEvent> Events { get; init; } = [];

    public IReadOnlyList<CollectedDv> Dvs { get; init; } = [];
}

/// <summary>
/// EC、SV、报警、CEID、DV 五个采集器放一起：启动时 Merge 一次生成五张编号表（跟 sc.xml 同目录），
/// CollectAll 一键采集。号段：ECID 10000–29999，SVID 30000–49999，ALID 50000–69999，CEID 70000–89999，
/// DVID 90000–99999。
/// </summary>
public sealed class GemCollectors
{
    /// <summary>
    /// 当前这套采集器；宿主启动时建出来即生效，冒烟与测试直接 new 一套。
    /// </summary>
    public static GemCollectors? Current { get; set; }

    public GemCollectors()
    {
        Current = this;
    }

    public EcCollector Ec { get; } = new();

    public SvCollector Sv { get; } = new();

    public AlarmCollector Alarm { get; } = new();

    public EventCollector Event { get; } = new();

    public DvCollector Dv { get; } = new();

    /// <summary>
    /// 启动时调一次，组件树装配完之后：各类合并自己的编号表，一张表出问题不耽误别的，设备照跑。
    /// 报警相关有先后：先分 ALID，再定报警事件带的 DV，再给报警生成报出/清除事件，最后把事件 CEID 回填进报警表。
    /// </summary>
    public void Merge(IReadOnlyList<ComponentBase> roots, string directory)
    {
        Run("ECID", () => Ec.Merge(roots, directory));
        Run("SVID", () => Sv.Merge(roots, directory));
        Run("ALID", () => Alarm.Merge(roots, directory));
        Run("DVID", () => Dv.Merge(directory, hasAlarmEvents: Alarm.WithEvents.Count > 0));
        Run("CEID", () => Event.Merge(roots, directory, Alarm.WithEvents, Dv.AlarmPayloadDvids));
        Run("ALID", () => Alarm.LinkEvents(Event));
    }

    /// <summary>
    /// 一键采集：全部在用的 EC、SV（带当前值）、报警定义、事件定义、DV 定义。
    /// </summary>
    public GemSnapshot CollectAll()
    {
        return new GemSnapshot
        {
            Ecs = Ec.Collect(),
            Svs = Sv.Collect(),
            Alarms = Alarm.Collect(),
            Events = Event.Collect(),
            Dvs = Dv.Collect(),
        };
    }

    private static void Run(string kind, Func<bool> merge)
    {
        try
        {
            merge();
        }
        catch (Exception exception)
        {
            LogHelper.Error(kind, $"编号表合并失败，本次不可用: {exception.Message}");
        }
    }
}
