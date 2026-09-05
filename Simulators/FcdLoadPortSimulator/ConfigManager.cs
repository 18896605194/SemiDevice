using System.IO;
using System.Text.Json;

namespace FcdLoadPortSimulator;

/// <summary>
/// 配置管理器：串口默认参数（config.json），与 responses.json 同目录。
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
            SerialPort = new SerialPortConfig
            {
                DefaultPort = "COM20",
                DefaultBaudRate = 9600,
                DefaultDataBits = 8,
                DefaultStopBits = 1,
                DefaultParity = "None",
            },
        };
    }
}

public class AppConfig
{
    public SerialPortConfig SerialPort { get; set; } = new();
}

public class SerialPortConfig
{
    public string DefaultPort { get; set; } = "COM20";
    public int DefaultBaudRate { get; set; } = 9600;
    public int DefaultDataBits { get; set; } = 8;
    public int DefaultStopBits { get; set; } = 1;
    public string DefaultParity { get; set; } = "None";
}
