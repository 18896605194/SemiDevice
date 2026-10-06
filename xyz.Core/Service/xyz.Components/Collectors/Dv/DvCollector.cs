using System.Reflection;
using xyz.Common.Log;
using xyz.Components.Attributes;

namespace xyz.Components.Collectors;

/// <summary>
/// DV 采集器：两种来源——组件上的 [DataVariable]（全名 = 组件全路径.代码），报警事件固定带的那几项（System.Alarm.*）。
/// </summary>
public sealed class DvCollector
{
    public const string FileName = "DvDefinitions.xml";
    public const int FirstId = 90000;
    public const int LastId = 99999;
    private const string Kind = "DVID";

    /// <summary>
    /// 报警事件（报出、清除）带的 DV，按这个顺序挂在事件上。
    /// </summary>
    public static IReadOnlyList<(string Name, string Format, string Description)> AlarmPayload { get; } =
    [
        ("System.Alarm.Alid", "Int", "报警编号 ALID"),
        ("System.Alarm.Name", "String", "报警全名（组件路径.报警代码）"),
        ("System.Alarm.Source", "String", "报警来源组件路径"),
        ("System.Alarm.Category", "String", "报警分类"),
        ("System.Alarm.AlarmLevel", "String", "报警等级"),
        ("System.Alarm.AlarmText", "String", "报警文本"),
    ];

    private volatile IReadOnlyList<DvDefinition> _definitions = Array.Empty<DvDefinition>();
    private volatile IReadOnlyList<int> _alarmPayloadDvids = Array.Empty<int>();
    private volatile IReadOnlyDictionary<string, int> _enabledDvids = new Dictionary<string, int>();
    private volatile IReadOnlyDictionary<int, DvDefinition> _byDvid = new Dictionary<int, DvDefinition>();

    /// <summary>
    /// 编号表全文（含停用的行），按编号排序；Merge 之前或表不可用时为空。
    /// </summary>
    public IReadOnlyList<DvDefinition> Definitions => _definitions;

    /// <summary>
    /// 报警事件带的 DVID，顺序同 <see cref="AlarmPayload"/>；没有报警事件或表不可用时为空。
    /// </summary>
    public IReadOnlyList<int> AlarmPayloadDvids => _alarmPayloadDvids;

    /// <summary>
    /// 启动时调一次（报警编号出来之后、事件编号之前）：扫组件树的 [DataVariable]，加上报警事件要带的几项，
    /// 合并 DV 编号表，有变化才写回。表坏了或写不进去时不覆盖，本次 DVID 不可用。
    /// hasAlarmEvents：有没有要生成事件的报警，没有就不需要报警 DV。返回是否写了盘。
    /// </summary>
    public bool Merge(IEnumerable<ComponentBase> roots, string directory, bool hasAlarmEvents)
    {
        _definitions = Array.Empty<DvDefinition>();
        _alarmPayloadDvids = Array.Empty<int>();
        _enabledDvids = new Dictionary<string, int>();
        _byDvid = new Dictionary<int, DvDefinition>();

        var declarations = new List<Declared>();
        if (hasAlarmEvents)
        {
            declarations.AddRange(AlarmPayload.Select(payload => new Declared(payload.Name, payload.Format, string.Empty, payload.Description)));
        }

        var names = new HashSet<string>(declarations.Select(declaration => declaration.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var declared in Scan(roots))
        {
            if (!names.Add(declared.Name))
            {
                LogHelper.Error(Kind, $"{declared.Name} 重复声明，只认第一个");
                continue;
            }

            declarations.Add(declared);
        }

        var byName = declarations.ToDictionary(declaration => declaration.Name, StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(directory, FileName);
        if (!DefinitionTable.TryLoad<DvDefinitionFile, DvDefinition>(path, out var rows, out var error))
        {
            LogHelper.Error(Kind, $"{path} {error}，不覆盖原文件，本次 DVID 不可用");
            return false;
        }

        var result = DefinitionTable.Merge(rows, declarations.Select(declaration => declaration.Name), FirstId, LastId,
            _ => new DvDefinition(),
            row => Refresh(row, byName[row.Name]),
            Kind);

        if (result.Changed)
        {
            try
            {
                DefinitionTable.Save<DvDefinitionFile, DvDefinition>(path, rows);
            }
            catch (Exception exception)
            {
                LogHelper.Error(Kind, $"{path} 写不进去，本次 DVID 不可用: {exception.Message}");
                return false;
            }
        }

        _definitions = rows;
        var enabled = rows.Where(row => row.Enabled).ToDictionary(row => row.Name, row => row.Id, StringComparer.OrdinalIgnoreCase);
        _enabledDvids = enabled;
        _byDvid = rows.Where(row => row.Enabled).ToDictionary(row => row.Id);
        if (hasAlarmEvents && AlarmPayload.All(payload => enabled.ContainsKey(payload.Name)))
        {
            _alarmPayloadDvids = AlarmPayload.Select(payload => enabled[payload.Name]).ToList();
        }

        LogHelper.Info(Kind, $"{FileName}：在用 {enabled.Count} 项（新增 {result.Added}，恢复 {result.Restored}，停用 {result.Disabled}）"
            + (result.Changed ? "，已写回" : "，无变化"));
        return result.Changed;
    }

    /// <summary>
    /// 按全名查在用 DV 的 DVID；没有或已停用返回 0。
    /// </summary>
    public int DvidOf(string name)
    {
        return _enabledDvids.TryGetValue(name, out var dvid) ? dvid : 0;
    }

    /// <summary>
    /// 按 DVID 查在用的 DV 定义；没有或已停用返回 null。
    /// </summary>
    public DvDefinition? ByDvid(int dvid)
    {
        return _byDvid.TryGetValue(dvid, out var row) ? row : null;
    }

    /// <summary>
    /// 一键采集：全部在用的 DV 定义。
    /// </summary>
    public IReadOnlyList<CollectedDv> Collect()
    {
        return _definitions.Where(row => row.Enabled).Select(row => new CollectedDv
        {
            Dvid = row.Id,
            Name = row.Name,
            Format = row.Format,
            Unit = row.Unit,
            Description = row.Description,
        }).ToList();
    }

    /// <summary>
    /// 代码里声明的一项 DV：全名、格式、单位、说明。
    /// </summary>
    private sealed record Declared(string Name, string Format, string Unit, string Description);

    /// <summary>
    /// 扫组件树上的 [DataVariable]：全名 = 组件全路径.DV 代码（字段或属性的值）。代码读不出来、全名重复的记日志后跳过。
    /// </summary>
    private static List<Declared> Scan(IEnumerable<ComponentBase> roots)
    {
        var result = new List<Declared>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in CollectorHelper.Walk(roots))
        {
            foreach (var member in component.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                var attribute = member.GetCustomAttribute<DataVariableAttribute>(inherit: true);
                if (attribute is null)
                {
                    continue;
                }

                var code = CollectorHelper.ReadCode(member, component);
                if (string.IsNullOrWhiteSpace(code))
                {
                    LogHelper.Error(Kind, $"{component.FullPath}.{member.Name} 的 DV 代码读不出来，不编号");
                    continue;
                }

                var name = $"{component.FullPath}.{code}";
                if (!names.Add(name))
                {
                    LogHelper.Error(Kind, $"{name} 重复声明，只认第一个");
                    continue;
                }

                result.Add(new Declared(name, attribute.Format.ToString(), attribute.Unit ?? string.Empty, attribute.Description));
            }
        }

        return result;
    }

    /// <summary>
    /// 格式、单位、说明以代码为准；编号、名字不动。
    /// </summary>
    private static bool Refresh(DvDefinition row, Declared declared)
    {
        bool changed = false;
        changed |= DefinitionTable.SetIfChanged(value => row.Format = value, row.Format, declared.Format);
        changed |= DefinitionTable.SetIfChanged(value => row.Unit = value, row.Unit, declared.Unit);
        changed |= DefinitionTable.SetIfChanged(value => row.Description = value, row.Description, declared.Description);
        return changed;
    }
}

/// <summary>
/// 采集到的一项 DV 定义：编号、全名、格式与说明（DV 的值只在事件报告时才有）。
/// </summary>
public sealed class CollectedDv
{
    public int Dvid { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}
