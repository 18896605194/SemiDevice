using xyz.Common.Log;
using xyz.Tools;

namespace xyz.Components.Collectors;

/// <summary>
/// 编号表的一行：编号、全名（组件全路径.名字）、代码里还有没有这一项。
/// </summary>
public interface IDefinitionRow
{
    int Id { get; set; }

    string Name { get; set; }

    bool Enabled { get; set; }
}

/// <summary>
/// 编号表文件的根：一张表就是一串行。
/// </summary>
public interface IDefinitionFile<TRow>
{
    List<TRow> Items { get; set; }
}

/// <summary>
/// 一次合并的结果：有没有变化、新分了几个号、恢复了几项（删掉又加回来的）、停用了几项。
/// </summary>
internal readonly record struct MergeResult(bool Changed, int Added, int Restored, int Disabled);

/// <summary>
/// 编号表（EcDefinitions.xml 等）的读、合并、写，四个采集器共用。规则跟 GR 一样：
/// 代码里有、表里也有的保号，元数据按代码刷新；代码里新增的在号段里接着往下分；
/// 代码里删掉的保号并置 Enabled=false，号永不回收。
/// </summary>
internal static class DefinitionTable
{
    /// <summary>
    /// 读编号表；文件不存在时是空表。读不出来、有空名或非法编号、名字或编号重复时返回 false：
    /// 这种表不能覆盖（外面可能已经按这些号配好了），这一类编号本次不可用。
    /// </summary>
    public static bool TryLoad<TFile, TRow>(string path, out List<TRow> rows, out string error)
        where TFile : class, IDefinitionFile<TRow>
        where TRow : IDefinitionRow
    {
        rows = new List<TRow>();
        error = string.Empty;
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            rows = XmlHelper.Deserialize<TFile>(path)?.Items ?? new List<TRow>();
        }
        catch (Exception exception)
        {
            error = $"读不出来：{exception.Message}";
            return false;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<int>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || row.Id <= 0)
            {
                error = $"有一行名字为空或编号非法（{row.Id} {row.Name}）";
                return false;
            }

            if (!names.Add(row.Name))
            {
                error = $"名字重复：{row.Name}";
                return false;
            }

            if (!ids.Add(row.Id))
            {
                error = $"编号重复：{row.Id}";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 按全名把代码里的声明合并进表（就地改 rows：新增的追加，删掉的置 Enabled=false，最后按编号排好）。
    /// declared 按扫描顺序给出、不重名，新号就按这个顺序分；号段用完的分不到号，记错误日志后跳过。
    /// create 给新名字建一行（编号、名字由这里填）；refresh 按代码刷新一行的元数据，返回是否有变化。
    /// </summary>
    public static MergeResult Merge<TRow>(List<TRow> rows, IEnumerable<string> declared, int firstId, int lastId,
        Func<string, TRow> create, Func<TRow, bool> refresh, string kind)
        where TRow : class, IDefinitionRow
    {
        var byName = rows.ToDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase);
        int nextId = firstId;
        foreach (var row in rows)
        {
            if (row.Id < firstId || row.Id > lastId)
            {
                LogHelper.Warn(kind, $"{row.Name} 的编号 {row.Id} 不在号段 {firstId}-{lastId} 内，保留原号");
            }
            else if (row.Id >= nextId)
            {
                nextId = row.Id + 1;
            }
        }

        bool changed = false;
        int added = 0;
        int restored = 0;
        var codeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in declared)
        {
            codeNames.Add(name);
            if (!byName.TryGetValue(name, out var row))
            {
                if (nextId > lastId)
                {
                    LogHelper.Error(kind, $"号段 {firstId}-{lastId} 已用完，{name} 分不到编号");
                    continue;
                }

                row = create(name);
                row.Id = nextId++;
                row.Name = name;
                rows.Add(row);
                byName.Add(name, row);
                added++;
                changed = true;
            }

            if (!row.Enabled)
            {
                row.Enabled = true;
                restored++;
                changed = true;
            }

            changed |= refresh(row);
        }

        int disabled = 0;
        foreach (var row in rows)
        {
            if (row.Enabled && !codeNames.Contains(row.Name))
            {
                row.Enabled = false;
                disabled++;
                changed = true;
            }
        }

        rows.Sort((left, right) => left.Id.CompareTo(right.Id));
        return new MergeResult(changed, added, restored, disabled);
    }

    /// <summary>
    /// 整表写回：先写临时文件再替换，写一半断电也不会留下半张表。
    /// </summary>
    public static void Save<TFile, TRow>(string path, List<TRow> rows)
        where TFile : class, IDefinitionFile<TRow>, new()
    {
        var temp = path + ".tmp";
        XmlHelper.Serialize(temp, new TFile { Items = rows });
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// 按字符串比较元数据，不同才改，返回是否改了。
    /// </summary>
    public static bool SetIfChanged(Action<string> assign, string oldValue, string? newValue)
    {
        newValue ??= string.Empty;
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return false;
        }

        assign(newValue);
        return true;
    }
}
