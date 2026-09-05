using System.IO;
using System.Text.Json;

namespace RejeRobotSimulator;

/// <summary>
/// 配置管理器：TCP 监听与延迟参数（config.json），与 log.txt 同目录。
/// </summary>
public class ConfigManager
{
    private readonly string _configFilePath;
    private AppConfig _config = new();

    public ConfigManager(string configDir)
    {
        _configFilePath = Path.Combine(configDir, "config.json");
        LoadConfig();
    }

    public AppConfig Config => _config;

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                _config = JsonSerializer.Deserialize<AppConfig>(json) ?? CreateDefaultConfig();
            }
            else
            {
                _config = CreateDefaultConfig();
                SaveConfig();
            }
        }
        catch
        {
            // 配置损坏时回落默认, 仿真器不因配置问题起不来
            _config = CreateDefaultConfig();
        }
    }

    public void SaveConfig()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_configFilePath, json);
        }
        catch
        {
            // 只读目录等场景保存失败可忽略, 下次启动重新生成
        }
    }

    private static AppConfig CreateDefaultConfig()
    {
        return new AppConfig
        {
            Port = 9000,
            AckDelayMs = 50,
            ResponseDelayMs = 50,
            MotionDelayMs = 3000,
            FailureRate = 0,
        };
    }
}

public class AppConfig
{
    /// <summary>TCP 监听端口</summary>
    public int Port { get; set; } = 9000;

    /// <summary>第一次回复 (ACK) 延迟 ms</summary>
    public int AckDelayMs { get; set; } = 50;

    /// <summary>第二次回复 (结果) 延迟 ms</summary>
    public int ResponseDelayMs { get; set; } = 50;

    /// <summary>运动动作仿真耗时 ms</summary>
    public int MotionDelayMs { get; set; } = 3000;

    /// <summary>运动动作仿真失败率 %</summary>
    public int FailureRate { get; set; } = 0;
}
