using System.Text.Json;
using xyz.Common.Helpers;
using xyz.Configs.Models;

namespace xyz.Configs;


public static class EC
{
    private static readonly object Gate = new();
    private static List<EcSettingConfig> _settings = new();
    private static Dictionary<string, EcSettingConfig> _pathIndex = new(StringComparer.Ordinal);
    private static Dictionary<string, EcValueConfig> _valueIndex = new(StringComparer.Ordinal);

    /// <summary>
    /// ec.xml 全路径，Flush 写回用。
    /// </summary>
    public static string FilePath { get; private set; } = string.Empty;

    /// <summary>
    /// 读取 ec.xml 并建索引；文件不存在时为空树（等合并器补建）。
    /// </summary>
    public static void Load()
    {
        lock (Gate)
        {
            FilePath = FindConfigFile();
            _settings = File.Exists(FilePath)
                ? XmlHelper.Deserialize<EcConfig>(FilePath)?.Settings ?? new List<EcSettingConfig>()
                : new List<EcSettingConfig>();
            RebuildIndex();
        }
    }

    /// <summary>
    /// 按完整路径读值（如 "LoadPort1"、"LoadTimeout"）；缺失返回空串。
    /// </summary>
    public static string GetByPath(string settingPath, string valueName)
    {
        lock (Gate)
        {
            return _valueIndex.TryGetValue(Identity(settingPath, valueName), out var value)
                ? value.Value ?? string.Empty
                : string.Empty;
        }
    }

    /// <summary>
    /// 修改一条 EC 值并写回（当前为同步写；在线编辑场景接入后可改防抖异步）。
    /// 返回是否有变化（值相同返回 false，不落盘）。
    /// </summary>
    public static bool SetValueByPath(string settingPath, string valueName, string newValue)
    {
        bool changed;
        lock (Gate)
        {
            if (!_valueIndex.TryGetValue(Identity(settingPath, valueName), out var value))
            {
                var setting = GetOrCreateSetting(settingPath);
                value = new EcValueConfig { Name = valueName, Value = newValue };
                setting.Values.Add(value);
                _valueIndex[Identity(settingPath, valueName)] = value;
                changed = true;
            }
            else if (string.Equals(value.Value, newValue, StringComparison.Ordinal))
            {
                changed = false;
            }
            else
            {
                value.Value = newValue;
                changed = true;
            }
        }

        if (changed)
        {
            Flush();
        }

        return changed;
    }

    /// <summary>
    /// 合并一条 EC 声明：不存在则创建（Value=initialValue）；已存在只更新元数据、不动 Value。
    /// 返回是否有变化。
    /// </summary>
    public static bool UpsertValueMetadata(string settingPath, string valueName, string initialValue,
        string? format, string? unit, string? min, string? max,
        string? defaultValue, string? description, string? options)
    {
        lock (Gate)
        {
            bool changed = false;

            if (!_valueIndex.TryGetValue(Identity(settingPath, valueName), out var value))
            {
                var setting = GetOrCreateSetting(settingPath);
                value = new EcValueConfig { Name = valueName, Value = initialValue };
                setting.Values.Add(value);
                _valueIndex[Identity(settingPath, valueName)] = value;
                changed = true;
            }

            changed |= SetIfChanged(v => value.Format = v, value.Format, format);
            changed |= SetIfChanged(v => value.Unit = v, value.Unit, unit);
            changed |= SetIfChanged(v => value.Min = v, value.Min, min);
            changed |= SetIfChanged(v => value.Max = v, value.Max, max);
            changed |= SetIfChanged(v => value.Default = v, value.Default, defaultValue);
            changed |= SetIfChanged(v => value.Description = v, value.Description, description);
            changed |= SetIfChanged(v => value.Options = v, value.Options, options);

            return changed;
        }
    }

    /// <summary>
    /// 把内存树写回 ec.xml。
    /// </summary>
    public static void Flush()
    {
        lock (Gate)
        {
            if (string.IsNullOrEmpty(FilePath) || _settings.Count == 0)
            {
                return;
            }

            XmlHelper.Serialize(FilePath, new EcConfig { Settings = _settings });
        }
    }

    #region 内部

    private static EcSettingConfig GetOrCreateSetting(string path)
    {
        var level = _settings;
        EcSettingConfig? current = null;
        var currentPath = string.Empty;

        foreach (var segment in path.Split('.'))
        {
            currentPath = currentPath.Length == 0 ? segment : currentPath + "." + segment;
            var next = level.FirstOrDefault(s => s.Name == segment);
            if (next is null)
            {
                next = new EcSettingConfig { Name = segment };
                level.Add(next);
                _pathIndex[currentPath] = next;
            }

            current = next;
            level = next.Children;
        }

        return current!;
    }

    private static void RebuildIndex()
    {
        _pathIndex = new Dictionary<string, EcSettingConfig>(StringComparer.Ordinal);
        _valueIndex = new Dictionary<string, EcValueConfig>(StringComparer.Ordinal);
        IndexLevel(_settings, string.Empty);
    }

    private static void IndexLevel(List<EcSettingConfig> settings, string parent)
    {
        foreach (var setting in settings)
        {
            var path = parent.Length == 0 ? setting.Name : parent + "." + setting.Name;
            _pathIndex[path] = setting;

            foreach (var value in setting.Values)
            {
                _valueIndex[Identity(path, value.Name)] = value;
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

    private static string Identity(string path, string name)
    {
        return path + "\0" + name;
    }

    private static string FindConfigFile()
    {
        var pathsFile = Path.Combine(AppContext.BaseDirectory, "Paths.json");
        if (File.Exists(pathsFile))
        {
            var configPath = ReadConfigPath();
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new InvalidOperationException("Paths.json 中未配置 ConfigPath");
            }

            return Path.Combine(Path.GetFullPath(configPath, AppContext.BaseDirectory), "ec.xml");
        }

        // 没有 Paths.json 时的兼容查找（与 SC 一致）
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ec.xml"),
            Path.Combine(AppContext.BaseDirectory, "Config", "ec.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "ec.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "Config", "ec.xml"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "xyz.Configs", "Config", "ec.xml")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[1];
    }

    private static string? ReadConfigPath()
    {
        var pathsFile = Path.Combine(AppContext.BaseDirectory, "Paths.json");
        if (!File.Exists(pathsFile))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(pathsFile));
        return document.RootElement.TryGetProperty("ConfigPath", out var value)
            ? value.GetString()
            : null;
    }

    #endregion
}
