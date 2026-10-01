using System.Globalization;
using System.Reflection;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// EC 管理组件。ec.xml 不随程序发布，是启动时照组件树生成的：组件上每个 [VariableMark(EC)] 属性一项，
/// 层级、先后都跟组件树（也就是 sc.xml）一样；文件已经有了就只补新的、更新说明和上下限，改过的值不动。
/// </summary>
[Component(description: "EC 管理组件（在线可调参数 ec.xml）")]
public class EcComponent : ComponentBase
{
    /// <summary>
    /// 当前 EC；sc.xml 里装出来即生效。没装时各组件的 EC 属性读回退 [VariableMark] 的默认值，写不生效。
    /// 冒烟与测试直接 new 一个：只在内存里，不读也不写 ec.xml。
    /// </summary>
    public static EcComponent? Current { get; set; }

    private readonly object _gate = new();
    private List<EcSettingConfig> _settings = new();

    /// <summary>"路径\0名字" → 值节点。</summary>
    private readonly Dictionary<string, EcValueConfig> _values = new(StringComparer.Ordinal);

    /// <summary>
    /// 组件树上声明了的项（"路径\0名字"），合并声明时定；还没合并过为 null（那时文件里有什么算什么）。
    /// ec.xml 里多出来的（组件从 sc.xml 拿掉了、属性从代码里删了）值留着不丢，但不给界面，界面也改不了。
    /// </summary>
    private HashSet<string>? _declared;

    public EcComponent()
    {
        Current = this;
    }

    /// <summary>
    /// ec.xml 全路径；空 = 只在内存里（直接 new 出来、没 Load 过），Flush 不写盘。
    /// </summary>
    public string FilePath { get; private set; } = string.Empty;

    /// <summary>
    /// 一项的值变了（界面改的、组件自己写的都算）：组件全路径 + 改完的值节点副本。在改值的那个线程上触发。
    /// </summary>
    public event Action<string, EcValueConfig>? ValueChanged;

    #region 装载

    /// <summary>
    /// 装配读完 SC 后读 ec.xml（跟 sc.xml 同一个配置目录）。
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        Load(System.IO.Path.Combine(SC.ConfigDirectory, "ec.xml"));
    }

    /// <summary>
    /// 读 ec.xml 并建索引，之后的改动都写回这个文件；文件不存在时为空树（等 Merge 补建）。
    /// </summary>
    public void Load(string filePath)
    {
        lock (_gate)
        {
            FilePath = filePath;
            _settings = File.Exists(filePath)
                ? XmlHelper.Deserialize<EcConfig>(filePath)?.Settings ?? new List<EcSettingConfig>()
                : new List<EcSettingConfig>();
            RebuildIndex();
        }
    }

    #endregion

    #region 合并声明

    /// <summary>
    /// 把组件树上全部 [VariableMark(EC)] 声明合并进来：缺的按声明的 Default 补建；
    /// 已有的只更新元数据（格式、单位、上下限、说明），不动值；节点和值的先后按组件树重排。
    /// 装配完成后调一次，有变化才写盘；返回是否写了盘。
    /// </summary>
    public bool Merge(IEnumerable<ComponentBase> roots)
    {
        var declarations = new List<(string Path, string Name, VariableMarkAttribute Mark)>();
        foreach (var root in roots)
        {
            CollectDeclarations(root, declarations);
        }

        int added = 0;
        int orphans;
        bool changed = false;
        lock (_gate)
        {
            foreach (var (path, name, mark) in declarations)
            {
                if (!_values.TryGetValue(Key(path, name), out var value))
                {
                    value = Add(path, name, mark.Default ?? string.Empty);
                    added++;
                    changed = true;
                }

                changed |= UpdateMetadata(value, mark);
            }

            var declared = declarations.Select(item => Key(item.Path, item.Name)).ToHashSet(StringComparer.Ordinal);
            _declared = declared;
            orphans = _values.Keys.Count(key => !declared.Contains(key));
            changed |= Reorder(declarations);
        }

        if (changed)
        {
            Flush();
            LogHelper.Info(Name, $"EC 声明合并完成（新增 {added} 项），已写回 {FilePath}");
        }

        if (orphans > 0)
        {
            LogHelper.Info(Name, $"ec.xml 里有 {orphans} 项在组件树上已经没有了（组件从 sc.xml 拿掉了，或属性从代码里删了）：值留着不动，界面不列");
        }

        return changed;
    }

    /// <summary>
    /// 收集一个组件及其各级子组件上的 EC 声明。路径取组件全路径，跟 GetEc*/SetEc* 读写时是同一个键。
    /// </summary>
    private static void CollectDeclarations(ComponentBase component,
        List<(string Path, string Name, VariableMarkAttribute Mark)> declarations)
    {
        foreach (var property in component.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var mark = property.GetCustomAttribute<VariableMarkAttribute>();
            if (mark is not null && mark.Type == VariableType.EC)
            {
                declarations.Add((component.FullPath, property.Name, mark));
            }
        }

        foreach (var child in component.Children)
        {
            CollectDeclarations(child, declarations);
        }
    }

    private static bool UpdateMetadata(EcValueConfig value, VariableMarkAttribute mark)
    {
        bool changed = false;
        changed |= SetIfChanged(v => value.Format = v, value.Format, mark.Format.ToString());
        changed |= SetIfChanged(v => value.Unit = v, value.Unit, mark.Unit);
        changed |= SetIfChanged(v => value.Min = v, value.Min, mark.Min);
        changed |= SetIfChanged(v => value.Max = v, value.Max, mark.Max);
        changed |= SetIfChanged(v => value.Default = v, value.Default, mark.Default);
        changed |= SetIfChanged(v => value.Description = v, value.Description, mark.Description);
        changed |= SetIfChanged(v => value.Options = v, value.Options, mark.Options);
        return changed;
    }

    /// <summary>
    /// 按组件树的先后重排 ec.xml：节点跟着组件走、节点里的值跟着声明走，文件的层级和先后就跟 sc.xml 一样
    /// （后加进 sc.xml 的组件不会一直挂在文件末尾）。树上已经没有的留在各层最后，原来的先后不变。调用方持锁；返回先后变了没有。
    /// </summary>
    private bool Reorder(List<(string Path, string Name, VariableMarkAttribute Mark)> declarations)
    {
        var nodeOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        var valueOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (path, name, _) in declarations)
        {
            valueOrder.TryAdd(Key(path, name), valueOrder.Count);

            // 这一项所在的节点和它的各级上级：按第一次出现排号，就是组件树的先后。
            for (int dot = path.IndexOf('.'); dot >= 0; dot = path.IndexOf('.', dot + 1))
            {
                nodeOrder.TryAdd(path[..dot], nodeOrder.Count);
            }

            nodeOrder.TryAdd(path, nodeOrder.Count);
        }

        return ReorderLevel(_settings, string.Empty, nodeOrder, valueOrder);
    }

    private static bool ReorderLevel(List<EcSettingConfig> settings, string parent,
        Dictionary<string, int> nodeOrder, Dictionary<string, int> valueOrder)
    {
        bool changed = Sort(settings, setting => nodeOrder.GetValueOrDefault(Join(parent, setting.Name), int.MaxValue));
        foreach (var setting in settings)
        {
            string path = Join(parent, setting.Name);
            changed |= Sort(setting.Values, value => valueOrder.GetValueOrDefault(Key(path, value.Name), int.MaxValue));
            changed |= ReorderLevel(setting.Children, path, nodeOrder, valueOrder);
        }

        return changed;
    }

    /// <summary>
    /// 按排号稳定排序（排号一样的保持原来的先后）；返回先后变了没有。
    /// </summary>
    private static bool Sort<T>(List<T> items, Func<T, int> order)
    {
        var sorted = items.OrderBy(order).ToList();
        if (sorted.SequenceEqual(items))
        {
            return false;
        }

        items.Clear();
        items.AddRange(sorted);
        return true;
    }

    #endregion

    #region 读写

    /// <summary>
    /// 按"组件全路径 + 名字"读值（如 "LoadPort1" + "LoadTimeout"）；缺失返回空串。
    /// </summary>
    public string Get(string path, string name)
    {
        lock (_gate)
        {
            return _values.TryGetValue(Key(path, name), out var value)
                ? value.Value ?? string.Empty
                : string.Empty;
        }
    }

    /// <summary>
    /// 改一项值（没有就新建）并写回 ec.xml，所有 live 读立即生效。组件自己的 EC 属性走这儿，不查格式和范围。
    /// 返回是否有变化（值相同返回 false，不落盘）。
    /// </summary>
    public bool Set(string path, string name, string value)
    {
        EcValueConfig? changed = null;
        lock (_gate)
        {
            if (!_values.TryGetValue(Key(path, name), out var existing))
            {
                existing = Add(path, name, value);
            }
            else if (string.Equals(existing.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
            else
            {
                existing.Value = value;
            }

            if (IsDeclared(path, name))
            {
                changed = Copy(existing);
            }
        }

        Flush();
        if (changed is not null)
        {
            ValueChanged?.Invoke(path, changed);
        }

        return true;
    }

    /// <summary>
    /// 界面改一项值：只认组件树上声明了的项，按声明的格式、上下限、可选值查过才改，改完写回 ec.xml，所有 live 读立即生效。
    /// 值按规整后的写法存（整数去掉前导零、布尔写 True / False、枚举按声明的大小写）。
    /// item 带回这一项（改成功是改完的，没改成是原来的，上下限、单位、可选值供调用方拼提示）；没有这一项时为 null。
    /// </summary>
    public EcSetResult TrySet(string path, string name, string value, out EcValueConfig? item)
    {
        item = null;
        lock (_gate)
        {
            if (!_values.TryGetValue(Key(path, name), out var existing) || !IsDeclared(path, name))
            {
                return EcSetResult.NotFound;
            }

            item = Copy(existing);
            var result = Normalize(existing, value, out string normalized);
            if (result != EcSetResult.Ok)
            {
                return result;
            }

            if (string.Equals(existing.Value, normalized, StringComparison.Ordinal))
            {
                return EcSetResult.Unchanged;
            }

            string? old = existing.Value;
            existing.Value = normalized;
            try
            {
                Flush();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 没写进文件就不算改了：内存里的值退回去，不然重启后悄悄变回旧值。
                existing.Value = old;
                LogHelper.Error(Name, $"EC 写不进 {FilePath}: {exception.Message}");
                return EcSetResult.SaveFailed;
            }

            item = Copy(existing);
        }

        ValueChanged?.Invoke(path, item);
        return EcSetResult.Ok;
    }

    /// <summary>
    /// 组件树上声明了的全部 EC 项（组件全路径 + 值节点副本），先后跟组件树一样，给界面列参数、取格式上下限单位用。
    /// 给副本不给原节点：原节点在锁里改，拿出去读会跟写并发。
    /// </summary>
    public IReadOnlyList<(string Path, EcValueConfig Value)> Snapshot()
    {
        lock (_gate)
        {
            var items = new List<(string Path, EcValueConfig Value)>();
            Collect(_settings, string.Empty, items);
            return items;
        }
    }

    private void Collect(List<EcSettingConfig> settings, string parent, List<(string Path, EcValueConfig Value)> items)
    {
        foreach (var setting in settings)
        {
            string path = Join(parent, setting.Name);
            foreach (var value in setting.Values)
            {
                if (IsDeclared(path, value.Name))
                {
                    items.Add((path, Copy(value)));
                }
            }

            Collect(setting.Children, path, items);
        }
    }

    private static EcValueConfig Copy(EcValueConfig value)
    {
        return new EcValueConfig
        {
            Name = value.Name,
            Description = value.Description,
            Format = value.Format,
            Min = value.Min,
            Max = value.Max,
            Value = value.Value,
            Default = value.Default,
            Unit = value.Unit,
            Options = value.Options,
        };
    }

    /// <summary>
    /// 整树写回 ec.xml；只在内存里（没有文件）或还是空树时不写。
    /// 先写临时文件再整个换上去：写到一半断电、崩溃，原来那份还是完整的——里面是现场调过的值。
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(FilePath) || _settings.Count == 0)
            {
                return;
            }

            string temporary = FilePath + ".tmp";
            XmlHelper.Serialize(temporary, new EcConfig { Settings = _settings });
            File.Move(temporary, FilePath, overwrite: true);
        }
    }

    #endregion

    #region 校验

    /// <summary>
    /// 按这一项声明的格式查 value，合法时给出规整后的写法。
    /// </summary>
    private static EcSetResult Normalize(EcValueConfig item, string value, out string normalized)
    {
        normalized = value;
        string text = value.Trim();
        if (!Enum.TryParse(item.Format, out ValueFormat format))
        {
            // 格式没写或不认识：当文本，不查。
            format = ValueFormat.String;
        }

        switch (format)
        {
            case ValueFormat.Int:
                // 组件按 int 读，超出 int 的数读回来是 0，所以这里也按 int 卡。
                if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int integer))
                {
                    return EcSetResult.InvalidFormat;
                }

                normalized = integer.ToString(CultureInfo.InvariantCulture);
                return InRange(integer, item) ? EcSetResult.Ok : EcSetResult.OutOfRange;

            case ValueFormat.Double:
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                    || !double.IsFinite(number))
                {
                    return EcSetResult.InvalidFormat;
                }

                normalized = number.ToString(CultureInfo.InvariantCulture);
                return InRange(number, item) ? EcSetResult.Ok : EcSetResult.OutOfRange;

            case ValueFormat.Bool:
                if (!bool.TryParse(text, out bool flag))
                {
                    return EcSetResult.InvalidFormat;
                }

                normalized = flag.ToString();
                return EcSetResult.Ok;

            case ValueFormat.Enum:
                string? option = (item.Options ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault(candidate => string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase));
                if (option is null)
                {
                    return EcSetResult.InvalidOption;
                }

                normalized = option;
                return EcSetResult.Ok;

            default:
                return EcSetResult.Ok;
        }
    }

    /// <summary>
    /// 上下限没声明（或写的不是数）就不限。
    /// </summary>
    private static bool InRange(double number, EcValueConfig item)
    {
        return !(TryParseLimit(item.Min, out double min) && number < min)
               && !(TryParseLimit(item.Max, out double max) && number > max);
    }

    private static bool TryParseLimit(string? text, out double limit)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out limit);
    }

    #endregion

    #region 内部

    /// <summary>
    /// 新建一项值（路径上缺的 Setting 节点一并补建）并进索引。调用方持锁。
    /// </summary>
    private EcValueConfig Add(string path, string name, string value)
    {
        var item = new EcValueConfig { Name = name, Value = value };
        GetOrCreateSetting(path).Values.Add(item);
        _values[Key(path, name)] = item;
        return item;
    }

    private EcSettingConfig GetOrCreateSetting(string path)
    {
        var level = _settings;
        EcSettingConfig? current = null;

        foreach (var segment in path.Split('.'))
        {
            var next = level.FirstOrDefault(s => s.Name == segment);
            if (next is null)
            {
                next = new EcSettingConfig { Name = segment };
                level.Add(next);
            }

            current = next;
            level = next.Children;
        }

        return current!;
    }

    private void RebuildIndex()
    {
        _values.Clear();
        IndexLevel(_settings, string.Empty);
    }

    private void IndexLevel(List<EcSettingConfig> settings, string parent)
    {
        foreach (var setting in settings)
        {
            var path = Join(parent, setting.Name);
            foreach (var value in setting.Values)
            {
                _values[Key(path, value.Name)] = value;
            }

            IndexLevel(setting.Children, path);
        }
    }

    private bool IsDeclared(string path, string name)
    {
        return _declared is null || _declared.Contains(Key(path, name));
    }

    private static bool SetIfChanged(Action<string?> assign, string? oldValue, string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return false;
        }

        assign(newValue);
        return true;
    }

    private static string Join(string parent, string name)
    {
        return parent.Length == 0 ? name : parent + "." + name;
    }

    private static string Key(string path, string name)
    {
        return path + "\0" + name;
    }

    #endregion
}
