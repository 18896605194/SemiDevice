using System.Reflection;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// EC 管理组件
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

    public EcComponent()
    {
        Current = this;
    }

    /// <summary>
    /// ec.xml 全路径；空 = 只在内存里（直接 new 出来、没 Load 过），Flush 不写盘。
    /// </summary>
    public string FilePath { get; private set; } = string.Empty;

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
    /// 已有的只更新元数据（格式、单位、上下限、说明），不动值。装配完成后调一次，有变化才写盘；返回是否写了盘。
    /// </summary>
    public bool Merge(IEnumerable<ComponentBase> roots)
    {
        var declarations = new List<(string Path, string Name, VariableMarkAttribute Mark)>();
        foreach (var root in roots)
        {
            CollectDeclarations(root, declarations);
        }

        int added = 0;
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
        }

        if (changed)
        {
            Flush();
            LogHelper.Info(Name, $"EC 声明合并完成（新增 {added} 项），已写回 {FilePath}");
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
    /// 改一项值（没有就新建）并写回 ec.xml，所有 live 读立即生效。
    /// 当前为同步写；在线编辑场景接入后可改防抖异步。返回是否有变化（值相同返回 false，不落盘）。
    /// </summary>
    public bool Set(string path, string name, string value)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(Key(path, name), out var existing))
            {
                Add(path, name, value);
            }
            else if (string.Equals(existing.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
            else
            {
                existing.Value = value;
            }
        }

        Flush();
        return true;
    }

    /// <summary>
    /// 整树写回 ec.xml；只在内存里（没有文件）或还是空树时不写。
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(FilePath) || _settings.Count == 0)
            {
                return;
            }

            XmlHelper.Serialize(FilePath, new EcConfig { Settings = _settings });
        }
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
            var path = parent.Length == 0 ? setting.Name : parent + "." + setting.Name;
            foreach (var value in setting.Values)
            {
                _values[Key(path, value.Name)] = value;
            }

            IndexLevel(setting.Children, path);
        }
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

    private static string Key(string path, string name)
    {
        return path + "\0" + name;
    }

    #endregion
}
