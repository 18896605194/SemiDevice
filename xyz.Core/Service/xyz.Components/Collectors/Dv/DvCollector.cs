using xyz.Common.Log;

namespace xyz.Components.Collectors;

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

/// <summary>
/// DV 采集器：维护 DvDefinitions.xml，分配 DVID（90000–99999）。目前的 DV 就是报警事件带的那 6 个，
/// 名字跟 GR 一样（System.Alarm.*）；只要有报警事件就在用，一个都没有时停用保号。
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

    /// <summary>
    /// 编号表全文（含停用的行），按编号排序；Merge 之前或表不可用时为空。
    /// </summary>
    public IReadOnlyList<DvDefinition> Definitions => _definitions;

    /// <summary>
    /// 报警事件带的 DVID，顺序同 <see cref="AlarmPayload"/>；没有报警事件或表不可用时为空。
    /// </summary>
    public IReadOnlyList<int> AlarmPayloadDvids => _alarmPayloadDvids;

    /// <summary>
    /// 启动时调一次（报警编号出来之后）：合并 DV 编号表，有变化才写回。表坏了或写不进去时不覆盖，本次 DVID 不可用。
    /// hasAlarmEvents：有没有要生成事件的报警，没有就不需要报警 DV。返回是否写了盘。
    /// </summary>
    public bool Merge(string directory, bool hasAlarmEvents)
    {
        _definitions = Array.Empty<DvDefinition>();
        _alarmPayloadDvids = Array.Empty<int>();

        var declarations = hasAlarmEvents ? AlarmPayload : [];
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
        if (hasAlarmEvents && AlarmPayload.All(payload => enabled.ContainsKey(payload.Name)))
        {
            _alarmPayloadDvids = AlarmPayload.Select(payload => enabled[payload.Name]).ToList();
        }

        LogHelper.Info(Kind, $"{FileName}：在用 {enabled.Count} 项（新增 {result.Added}，恢复 {result.Restored}，停用 {result.Disabled}）"
            + (result.Changed ? "，已写回" : "，无变化"));
        return result.Changed;
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

    private static bool Refresh(DvDefinition row, (string Name, string Format, string Description) declaration)
    {
        bool changed = false;
        changed |= DefinitionTable.SetIfChanged(value => row.Format = value, row.Format, declaration.Format);
        changed |= DefinitionTable.SetIfChanged(value => row.Description = value, row.Description, declaration.Description);
        return changed;
    }
}
