using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Collectors;

/// <summary>
/// 采集到的一项 SV：编号、全名、当前值和元数据。
/// </summary>
public sealed class CollectedSv
{
    public int Svid { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public string Min { get; init; } = string.Empty;

    public string Max { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool Visible { get; init; }
}

/// <summary>
/// SV 采集器：启动时把组件树上的 [VariableMark(SV)] 合并进 SvDefinitions.xml，分配 SVID（30000–49999）；
/// Collect 一次取全部 SV 的编号和当前值（直接读组件属性）。
/// </summary>
public sealed class SvCollector
{
    public const string FileName = "SvDefinitions.xml";
    public const int FirstId = 30000;
    public const int LastId = 49999;
    private const string Kind = "SVID";

    private volatile IReadOnlyList<SvDefinition> _definitions = Array.Empty<SvDefinition>();
    private volatile IReadOnlyList<(SvDefinition Row, VariableDeclaration Declaration)> _items = [];

    /// <summary>
    /// 编号表全文（含代码里已删、Enabled=False 的行），按编号排序；Merge 之前或表不可用时为空。
    /// </summary>
    public IReadOnlyList<SvDefinition> Definitions => _definitions;

    /// <summary>
    /// 启动时调一次：扫组件树 → 合并编号表 → 有变化才写回。表坏了或写不进去时不覆盖，本次 SVID 不可用。
    /// 返回是否写了盘。
    /// </summary>
    public bool Merge(IEnumerable<ComponentBase> roots, string directory)
    {
        _definitions = Array.Empty<SvDefinition>();
        _items = [];

        var declarations = CollectorHelper.ScanVariables(roots, VariableType.SV, Kind);
        var byName = declarations.ToDictionary(declaration => declaration.Name, StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(directory, FileName);
        if (!DefinitionTable.TryLoad<SvDefinitionFile, SvDefinition>(path, out var rows, out var error))
        {
            LogHelper.Error(Kind, $"{path} {error}，不覆盖原文件，本次 SVID 不可用");
            return false;
        }

        var result = DefinitionTable.Merge(rows, declarations.Select(declaration => declaration.Name), FirstId, LastId,
            name => new SvDefinition { Visible = byName[name].Mark.Visible },
            row => Refresh(row, byName[row.Name].Mark),
            Kind);

        if (result.Changed)
        {
            try
            {
                DefinitionTable.Save<SvDefinitionFile, SvDefinition>(path, rows);
            }
            catch (Exception exception)
            {
                LogHelper.Error(Kind, $"{path} 写不进去，本次 SVID 不可用: {exception.Message}");
                return false;
            }
        }

        _definitions = rows;
        _items = rows.Where(row => row.Enabled && byName.ContainsKey(row.Name))
            .Select(row => (row, byName[row.Name]))
            .ToList();
        LogHelper.Info(Kind, $"{FileName}：在用 {_items.Count} 项（新增 {result.Added}，恢复 {result.Restored}，停用 {result.Disabled}）"
            + (result.Changed ? "，已写回" : "，无变化"));
        return result.Changed;
    }

    /// <summary>
    /// 一键采集：全部在用的 SV（有编号的），带当前值。
    /// </summary>
    public IReadOnlyList<CollectedSv> Collect()
    {
        return _items.Select(item => new CollectedSv
        {
            Svid = item.Row.Id,
            Name = item.Row.Name,
            Value = CollectorHelper.ReadValue(item.Declaration, Kind),
            Format = item.Row.Format,
            Unit = item.Row.Unit,
            Min = item.Row.Min,
            Max = item.Row.Max,
            Description = item.Row.Description,
            Visible = item.Row.Visible,
        }).ToList();
    }

    /// <summary>
    /// 元数据以代码为准；编号、名字、Visible 不动。
    /// </summary>
    private static bool Refresh(SvDefinition row, VariableMarkAttribute mark)
    {
        bool changed = false;
        changed |= DefinitionTable.SetIfChanged(value => row.Format = value, row.Format, mark.Format.ToString());
        changed |= DefinitionTable.SetIfChanged(value => row.Unit = value, row.Unit, mark.Unit);
        changed |= DefinitionTable.SetIfChanged(value => row.Min = value, row.Min, mark.Min);
        changed |= DefinitionTable.SetIfChanged(value => row.Max = value, row.Max, mark.Max);
        changed |= DefinitionTable.SetIfChanged(value => row.Description = value, row.Description, mark.Description);
        return changed;
    }
}
