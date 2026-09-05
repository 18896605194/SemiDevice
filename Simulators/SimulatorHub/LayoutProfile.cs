using System.IO;
using System.Text.Json;

namespace SimulatorHub;

/// <summary>布局里的一台仿真实例。</summary>
public sealed class InstanceLayout
{
    /// <summary>类型键: "Robot" / "Lp300" (对应 MainWindow 的 Type* 常量)。</summary>
    public string Type { get; set; } = "";

    /// <summary>实例名 = instances 下的目录名 (如 robot-1), 还原布局时原样复用。</summary>
    public string Instance { get; set; } = "";

    /// <summary>端口: Robot 是 TCP 端口号, Lp300 是串口名 (如 9000 / COM3)。</summary>
    public string Port { get; set; } = "";

    /// <summary>应用预设时是否自动开口 (开始监听 / 打开串口)。</summary>
    public bool AutoOpen { get; set; } = true;
}

/// <summary>一份布局预设: 若干实例的组合。</summary>
public sealed class LayoutProfile
{
    public string Name { get; set; } = "";
    public List<InstanceLayout> Instances { get; set; } = new();
}

/// <summary>预设 JSON 存取 (profiles\*.json 与 last-layout.json 共用)。</summary>
public static class LayoutStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 中文预设名不转义
    };

    public static LayoutProfile? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<LayoutProfile>(File.ReadAllText(path));
        }
        catch
        {
            return null;   // 坏文件按"没有"处理, 不挡启动
        }
    }

    public static void Save(string path, LayoutProfile profile)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOpts));
    }
}
