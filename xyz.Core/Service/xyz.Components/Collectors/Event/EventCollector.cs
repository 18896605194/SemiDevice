using System.Reflection;
using xyz.Common.Log;
using xyz.Components.Attributes;

namespace xyz.Components.Collectors;

/// <summary>
/// 采集到的一项事件定义：编号、全名、文本、描述和报告时带的 DVID。
/// </summary>
public sealed class CollectedEvent
{
    public int Ceid { get; init; }

    public string Name { get; init; } = string.Empty;

    public string EventText { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<int> Dvids { get; init; } = [];
}

/// <summary>
/// 事件（CEID）采集器：启动时把组件树上的 [EventAttribut] 和报警的报出/清除事件合并进 EventDefinitions.xml，
/// 分配 CEID（70000–89999）；Collect 一次取全部事件定义。
/// 报警事件每条报警两个（Warn 级不生成），名字按 ALID 起（System.Alarm.{ALID}.Set/Clear，跟 GR 一样），
/// 报告时带 DvCollector 的报警 DV。
/// </summary>
public sealed class EventCollector
{
    public const string FileName = "EventDefinitions.xml";
    public const int FirstId = 70000;
    public const int LastId = 89999;
    private const string Kind = "CEID";

    private volatile IReadOnlyList<EventDefinition> _definitions = Array.Empty<EventDefinition>();
    private volatile IReadOnlyDictionary<string, int> _enabledCeids = new Dictionary<string, int>();

    /// <summary>
    /// 编号表全文（含停用的行），按编号排序；Merge 之前或表不可用时为空。
    /// </summary>
    public IReadOnlyList<EventDefinition> Definitions => _definitions;

    /// <summary>
    /// 报警报出/清除事件的全名。
    /// </summary>
    public static string AlarmEventName(int alid, bool clear)
    {
        return $"System.Alarm.{alid}.{(clear ? "Clear" : "Set")}";
    }

    /// <summary>
    /// 按全名查在用事件的 CEID；没有或已停用返回 0。
    /// </summary>
    public int CeidOf(string name)
    {
        return _enabledCeids.TryGetValue(name, out var ceid) ? ceid : 0;
    }

    /// <summary>
    /// 启动时调一次（报警、DV 编号出来之后）：扫组件树的 [EventAttribut]，再给 alarms 里每条报警生成报出/清除两个事件，
    /// 合并编号表，有变化才写回。表坏了或写不进去时不覆盖，本次 CEID 不可用。返回是否写了盘。
    /// alarms：要生成事件的报警（在用、非 Warn）；alarmPayload：报警事件带的 DVID。
    /// </summary>
    public bool Merge(IEnumerable<ComponentBase> roots, string directory,
        IReadOnlyList<AlarmDefinition>? alarms = null, IReadOnlyList<int>? alarmPayload = null)
    {
        _definitions = Array.Empty<EventDefinition>();
        _enabledCeids = new Dictionary<string, int>();

        var declarations = Scan(roots);
        declarations.AddRange(AlarmEvents(alarms ?? [], alarmPayload ?? []));
        var byName = declarations.ToDictionary(declaration => declaration.Name, StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(directory, FileName);
        if (!DefinitionTable.TryLoad<EventDefinitionFile, EventDefinition>(path, out var rows, out var error))
        {
            LogHelper.Error(Kind, $"{path} {error}，不覆盖原文件，本次 CEID 不可用");
            return false;
        }

        var result = DefinitionTable.Merge(rows, declarations.Select(declaration => declaration.Name), FirstId, LastId,
            _ => new EventDefinition(),
            row => Refresh(row, byName[row.Name]),
            Kind);

        if (result.Changed)
        {
            try
            {
                DefinitionTable.Save<EventDefinitionFile, EventDefinition>(path, rows);
            }
            catch (Exception exception)
            {
                LogHelper.Error(Kind, $"{path} 写不进去，本次 CEID 不可用: {exception.Message}");
                return false;
            }
        }

        _definitions = rows;
        _enabledCeids = rows.Where(row => row.Enabled)
            .ToDictionary(row => row.Name, row => row.Id, StringComparer.OrdinalIgnoreCase);
        LogHelper.Info(Kind, $"{FileName}：在用 {_enabledCeids.Count} 项（新增 {result.Added}，恢复 {result.Restored}，停用 {result.Disabled}）"
            + (result.Changed ? "，已写回" : "，无变化"));
        return result.Changed;
    }

    /// <summary>
    /// 一键采集：全部在用的事件定义（有编号的）。
    /// </summary>
    public IReadOnlyList<CollectedEvent> Collect()
    {
        return _definitions.Where(row => row.Enabled).Select(row => new CollectedEvent
        {
            Ceid = row.Id,
            Name = row.Name,
            EventText = row.EventText,
            Description = row.Description,
            Dvids = row.Payloads.Select(payload => payload.Dvid).ToList(),
        }).ToList();
    }

    /// <summary>
    /// 代码里声明的一个事件：全名、文本、描述、报告时带的 DVID。
    /// </summary>
    private sealed record Declared(string Name, string EventText, string? Description, IReadOnlyList<int> Dvids);

    /// <summary>
    /// 扫组件树上的 [EventAttribut]：全名 = 组件全路径.事件代码（字段或属性的值）。代码为空、全名重复的记日志后跳过。
    /// </summary>
    private static List<Declared> Scan(IEnumerable<ComponentBase> roots)
    {
        var result = new List<Declared>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in CollectorHelper.Walk(roots))
        {
            foreach (var member in component.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                var attribute = member.GetCustomAttribute<EventAttribut>(inherit: true);
                if (attribute is null)
                {
                    continue;
                }

                var code = CollectorHelper.ReadCode(member, component);
                if (string.IsNullOrWhiteSpace(code))
                {
                    LogHelper.Error(Kind, $"{component.FullPath}.{member.Name} 的事件代码读不出来，不编号");
                    continue;
                }

                var name = $"{component.FullPath}.{code}";
                if (!names.Add(name))
                {
                    LogHelper.Error(Kind, $"{name} 重复声明，只认第一个");
                    continue;
                }

                result.Add(new Declared(name, attribute.EventText, attribute.Description, []));
            }
        }

        return result;
    }

    /// <summary>
    /// 每条报警一对报出/清除事件，按 ALID 排；新号也按这个顺序分。
    /// </summary>
    private static IEnumerable<Declared> AlarmEvents(IReadOnlyList<AlarmDefinition> alarms, IReadOnlyList<int> payload)
    {
        foreach (var alarm in alarms.OrderBy(alarm => alarm.Id))
        {
            yield return new Declared(AlarmEventName(alarm.Id, clear: false), "报警报出",
                $"报警 {alarm.Id} 报出：{alarm.Name}（{alarm.AlarmText}）", payload);
            yield return new Declared(AlarmEventName(alarm.Id, clear: true), "报警清除",
                $"报警 {alarm.Id} 清除：{alarm.Name}（{alarm.AlarmText}）", payload);
        }
    }

    /// <summary>
    /// 文本、描述、带的 DV 以代码为准；编号、名字不动。
    /// </summary>
    private static bool Refresh(EventDefinition row, Declared declared)
    {
        bool changed = false;
        changed |= DefinitionTable.SetIfChanged(value => row.EventText = value, row.EventText, declared.EventText);
        changed |= DefinitionTable.SetIfChanged(value => row.Description = value, row.Description, declared.Description);
        if (!row.Payloads.Select(payload => payload.Dvid).SequenceEqual(declared.Dvids))
        {
            row.Payloads = declared.Dvids.Select(dvid => new EventPayload { Dvid = dvid }).ToList();
            changed = true;
        }

        return changed;
    }
}
