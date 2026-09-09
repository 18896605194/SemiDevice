using System.Text.Json;
using xyz.Tools;
using xyz.Configs.Models;

namespace xyz.Configs;

/// <summary>
/// SC 配置文件读取器（先解析模块级 Setting 节点）。
/// </summary>
public static class SC
{
    public static IReadOnlyList<ModuleConfig> Load()
    {
        var path = FindConfigFile();
        var config = XmlHelper.Deserialize<ScConfig>(path);
        return config?.Modules ?? [];
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

            var fullPath = Path.GetFullPath(configPath, AppContext.BaseDirectory);
            var scFile = Path.Combine(fullPath, "sc.xml");
            if (!File.Exists(scFile))
            {
                throw new FileNotFoundException($"配置目录未找到 sc.xml: {fullPath}");
            }

            return scFile;
        }

        // 没有 Paths.json 时的兼容查找
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "sc.xml"),
            Path.Combine(AppContext.BaseDirectory, "Config", "sc.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "sc.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "Config", "sc.xml"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "xyz.Configs", "Config", "sc.xml")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("未找到 sc.xml 配置文件");
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
}
