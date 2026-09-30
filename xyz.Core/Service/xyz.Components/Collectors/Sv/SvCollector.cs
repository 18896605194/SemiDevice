using System.Globalization;
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
/// 能画成曲线的一项 SV（布尔、整数、浮点、枚举）：全名、所在组件、格式、单位、说明，Read 现读当前值——
/// 布尔记 0/1、枚举记它的数值，读不出来或不是有限数给 null。
/// </summary>
public sealed record NumericSv(string Name, ComponentBase Owner, ValueFormat Format, string Unit, string Description, Func<double?> Read);

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
    /// 能画成曲线的在用 SV（字符串类的不算），顺序同编号表；数据曲线每秒按它取一行。
    /// </summary>
    public IReadOnlyList<NumericSv> NumericItems()
    {
        return _items
            .Where(item => item.Declaration.Mark.Format != ValueFormat.String)
            .Select(item => new NumericSv(item.Row.Name, item.Declaration.Owner, item.Declaration.Mark.Format, item.Row.Unit,
                item.Row.Description, () => ReadNumber(item.Declaration)))
            .ToList();
    }

    /// <summary>
    /// 每秒都要读，读失败不记日志（记了就是每秒一条），直接给 null。
    /// </summary>
    private static double? ReadNumber(VariableDeclaration declaration)
    {
        try
        {
            double? value = declaration.Property.GetValue(declaration.Owner) switch
            {
                bool flag => flag ? 1 : 0,
                Enum member => Convert.ToDouble(member, CultureInfo.InvariantCulture),
                IConvertible number and not string => number.ToDouble(CultureInfo.InvariantCulture),
                _ => null,
            };
            return value is { } finite && double.IsFinite(finite) ? finite : null;
        }
        catch
        {
            return null;
        }
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
