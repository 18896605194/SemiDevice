using System.Reflection;
using xyz.Common.Log;
using xyz.Components.Alarm;

namespace xyz.Components.Collectors;

/// <summary>
/// 采集到的一项报警定义：编号、全名、文本、分类、等级、描述、处理建议和报出/清除事件的 CEID。
/// </summary>
public sealed class CollectedAlarm
{
    public int Alid { get; init; }

    public string Name { get; init; } = string.Empty;

    public string AlarmText { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string AlarmLevel { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Solution { get; init; } = string.Empty;

    public int SetEventId { get; init; }

    public int ClearEventId { get; init; }
}

/// <summary>
/// 报警采集器：启动时把组件树上的 [Alarm] 合并进 AlarmDefinitions.xml，分配 ALID（50000–69999）；
/// 事件编号出来后再把每条报警报出/清除事件的 CEID 回填进表（Warn 级不生成事件，填 0）。
/// Collect 一次取全部报警定义。只管编号，报警的报出与复位还是 AlarmComponent 的事。
/// </summary>
public sealed class AlarmCollector
{
    public const string FileName = "AlarmDefinitions.xml";
    public const int FirstId = 50000;
    public const int LastId = 69999;
    private const string Kind = "ALID";

    private string _path = string.Empty;
    private List<AlarmDefinition>? _rows;
    private volatile IReadOnlyList<AlarmDefinition> _withEvents = Array.Empty<AlarmDefinition>();

    /// <summary>
    /// 编号表全文（含代码里已删、Enabled=False 的行），按编号排序；Merge 之前或表不可用时为空。
    /// </summary>
    public IReadOnlyList<AlarmDefinition> Definitions => _rows ?? (IReadOnlyList<AlarmDefinition>)Array.Empty<AlarmDefinition>();

    /// <summary>
    /// 要生成报出/清除事件的报警：在用、非 Warn 级，按 ALID 排序。
    /// </summary>
    public IReadOnlyList<AlarmDefinition> WithEvents => _withEvents;

    /// <summary>
    /// 启动时调一次：扫组件树 → 合并编号表 → 有变化才写回。表坏了或写不进去时不覆盖，本次 ALID 不可用。
    /// 返回是否写了盘。
    /// </summary>
    public bool Merge(IEnumerable<ComponentBase> roots, string directory)
    {
        _rows = null;
        _withEvents = Array.Empty<AlarmDefinition>();

        var declarations = Scan(roots);
        var byName = declarations.ToDictionary(declaration => declaration.Name, StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(directory, FileName);
        if (!DefinitionTable.TryLoad<AlarmDefinitionFile, AlarmDefinition>(path, out var rows, out var error))
        {
            LogHelper.Error(Kind, $"{path} {error}，不覆盖原文件，本次 ALID 不可用");
            return false;
        }

        var result = DefinitionTable.Merge(rows, declarations.Select(declaration => declaration.Name), FirstId, LastId,
            _ => new AlarmDefinition(),
            row => Refresh(row, byName[row.Name].Attribute),
            Kind);

        if (result.Changed)
        {
            try
            {
                DefinitionTable.Save<AlarmDefinitionFile, AlarmDefinition>(path, rows);
            }
            catch (Exception exception)
            {
                LogHelper.Error(Kind, $"{path} 写不进去，本次 ALID 不可用: {exception.Message}");
                return false;
            }
        }

        _path = path;
        _rows = rows;
        _withEvents = rows.Where(row => row.Enabled && byName.TryGetValue(row.Name, out var declared)
                                        && declared.Attribute.AlarmLevel != AlarmLevel.Warn).ToList();
        LogHelper.Info(Kind, $"{FileName}：在用 {rows.Count(row => row.Enabled)} 项（新增 {result.Added}，恢复 {result.Restored}，停用 {result.Disabled}）"
            + (result.Changed ? "，已写回" : "，无变化"));
        return result.Changed;
    }

    /// <summary>
    /// 事件编号出来之后调：把每条报警报出/清除事件的 CEID 回填进表（没有事件的填 0），有变化才写回。返回是否写了盘。
    /// </summary>
    public bool LinkEvents(EventCollector events)
    {
        var rows = _rows;
        if (rows is null)
        {
            return false;
        }

        bool changed = false;
        foreach (var row in rows)
        {
            int set = row.Enabled ? events.CeidOf(EventCollector.AlarmEventName(row.Id, clear: false)) : 0;
            int clear = row.Enabled ? events.CeidOf(EventCollector.AlarmEventName(row.Id, clear: true)) : 0;
            if (row.SetEventId != set || row.ClearEventId != clear)
            {
                row.SetEventId = set;
                row.ClearEventId = clear;
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        try
        {
            DefinitionTable.Save<AlarmDefinitionFile, AlarmDefinition>(_path, rows);
        }
        catch (Exception exception)
        {
            LogHelper.Error(Kind, $"{_path} 回填事件编号写不进去: {exception.Message}");
            return false;
        }

        LogHelper.Info(Kind, $"{FileName}：已回填报出/清除事件编号");
        return true;
    }

    /// <summary>
    /// 一键采集：全部在用的报警定义（有编号的），带报出/清除事件的 CEID。
    /// </summary>
    public IReadOnlyList<CollectedAlarm> Collect()
    {
        return Definitions.Where(row => row.Enabled).Select(row => new CollectedAlarm
        {
            Alid = row.Id,
            Name = row.Name,
            AlarmText = row.AlarmText,
            Category = row.Category,
            AlarmLevel = row.AlarmLevel,
            Description = row.Description,
            Solution = row.Solution,
            SetEventId = row.SetEventId,
            ClearEventId = row.ClearEventId,
        }).ToList();
    }

    /// <summary>
    /// 扫组件树上的 [Alarm]：全名 = 组件全路径.报警代码（字段或属性的值）。代码为空、全名重复的记日志后跳过。
    /// </summary>
    private static List<(string Name, AlarmAttribute Attribute)> Scan(IEnumerable<ComponentBase> roots)
    {
        var result = new List<(string Name, AlarmAttribute Attribute)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in CollectorHelper.Walk(roots))
        {
            foreach (var member in component.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                var attribute = member.GetCustomAttribute<AlarmAttribute>(inherit: true);
                if (attribute is null)
                {
                    continue;
                }

                var code = CollectorHelper.ReadCode(member, component);
                if (string.IsNullOrWhiteSpace(code))
                {
                    LogHelper.Error(Kind, $"{component.FullPath}.{member.Name} 的报警代码读不出来，不编号");
                    continue;
                }

                var name = $"{component.FullPath}.{code}";
                if (!names.Add(name))
                {
                    LogHelper.Error(Kind, $"{name} 重复声明，只认第一个");
                    continue;
                }

                result.Add((name, attribute));
            }
        }

        return result;
    }

    /// <summary>
    /// 文本、分类、等级、描述、处理建议都以代码为准；编号、名字、事件编号不动（事件编号由 LinkEvents 管）。
    /// </summary>
    private static bool Refresh(AlarmDefinition row, AlarmAttribute attribute)
    {
        bool changed = false;
        changed |= DefinitionTable.SetIfChanged(value => row.Category = value, row.Category, attribute.Category.ToString());
        changed |= DefinitionTable.SetIfChanged(value => row.AlarmLevel = value, row.AlarmLevel, attribute.AlarmLevel.ToString());
        changed |= DefinitionTable.SetIfChanged(value => row.AlarmText = value, row.AlarmText, attribute.AlarmText);
        changed |= DefinitionTable.SetIfChanged(value => row.Description = value, row.Description, attribute.Description);
        changed |= DefinitionTable.SetIfChanged(value => row.Solution = value, row.Solution, attribute.Solution);
        return changed;
    }
}
