using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Collectors;

/// <summary>
/// 采集到的一项 EC：编号、全名、当前值和元数据。
/// </summary>
public sealed class CollectedEc
{
    public int Ecid { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Format { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public string Min { get; init; } = string.Empty;

    public string Max { get; init; } = string.Empty;

    public string Default { get; init; } = string.Empty;

    public string Options { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool Visible { get; init; }
}

/// <summary>
/// EC 采集器：启动时把组件树上的 [VariableMark(EC)] 合并进 EcDefinitions.xml，分配 ECID（10000–29999）；
/// Collect 一次取全部 EC 的编号和当前值。EC 的值在 ec.xml，由 EcComponent 管，这里只读不写。
/// </summary>
public sealed class EcCollector
{
    public const string FileName = "EcDefinitions.xml";
    public const int FirstId = 10000;
    public const int LastId = 29999;
    private const string Kind = "ECID";

    private volatile IReadOnlyList<EcDefinition> _definitions = Array.Empty<EcDefinition>();
    private volatile IReadOnlyList<(EcDefinition Row, VariableDeclaration Declaration)> _items = [];

    /// <summary>
    /// 编号表全文（含代码里已删、Enabled=False 的行），按编号排序；Merge 之前或表不可用时为空。
    /// </summary>
    public IReadOnlyList<EcDefinition> Definitions => _definitions;

    /// <summary>
    /// 启动时调一次：扫组件树 → 合并编号表 → 有变化才写回。表坏了或写不进去时不覆盖，本次 ECID 不可用。
    /// 返回是否写了盘。
    /// </summary>
    public bool Merge(IEnumerable<ComponentBase> roots, string directory)
    {
        _definitions = Array.Empty<EcDefinition>();
        _items = [];

        var declarations = CollectorHelper.ScanVariables(roots, VariableType.EC, Kind);
        var byName = declarations.ToDictionary(declaration => declaration.Name, StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(directory, FileName);
        if (!DefinitionTable.TryLoad<EcDefinitionFile, EcDefinition>(path, out var rows, out var error))
        {
            LogHelper.Error(Kind, $"{path} {error}，不覆盖原文件，本次 ECID 不可用");
            return false;
        }

        var result = DefinitionTable.Merge(rows, declarations.Select(declaration => declaration.Name), FirstId, LastId,
            name => new EcDefinition { Visible = byName[name].Mark.Visible },
            row => Refresh(row, byName[row.Name].Mark),
            Kind);

        if (result.Changed)
        {
            try
            {
                DefinitionTable.Save<EcDefinitionFile, EcDefinition>(path, rows);
            }
            catch (Exception exception)
            {
                LogHelper.Error(Kind, $"{path} 写不进去，本次 ECID 不可用: {exception.Message}");
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
    /// 一键采集：全部在用的 EC（有编号的），带当前值（按属性读，读到的就是组件实际用的值）。
    /// </summary>
    public IReadOnlyList<CollectedEc> Collect()
    {
        return _items.Select(item => new CollectedEc
        {
            Ecid = item.Row.Id,
            Name = item.Row.Name,
            Value = CollectorHelper.ReadValue(item.Declaration, Kind),
            Format = item.Row.Format,
            Unit = item.Row.Unit,
            Min = item.Row.Min,
            Max = item.Row.Max,
            Default = item.Row.Default,
            Options = item.Row.Options,
            Description = item.Row.Description,
            Visible = item.Row.Visible,
        }).ToList();
    }

    /// <summary>
    /// 元数据以代码为准；编号、名字、Visible 不动。
    /// </summary>
    private static bool Refresh(EcDefinition row, VariableMarkAttribute mark)
    {
        bool changed = false;
        changed |= DefinitionTable.SetIfChanged(value => row.Format = value, row.Format, mark.Format.ToString());
        changed |= DefinitionTable.SetIfChanged(value => row.Unit = value, row.Unit, mark.Unit);
        changed |= DefinitionTable.SetIfChanged(value => row.Min = value, row.Min, mark.Min);
        changed |= DefinitionTable.SetIfChanged(value => row.Max = value, row.Max, mark.Max);
        changed |= DefinitionTable.SetIfChanged(value => row.Default = value, row.Default, mark.Default);
        changed |= DefinitionTable.SetIfChanged(value => row.Options = value, row.Options, mark.Options);
        changed |= DefinitionTable.SetIfChanged(value => row.Description = value, row.Description, mark.Description);
        return changed;
    }
}
