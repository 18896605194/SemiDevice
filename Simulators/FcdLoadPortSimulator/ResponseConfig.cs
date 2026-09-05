using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FcdLoadPortSimulator;

/// <summary>
/// 应答表一行的模型 (responses.json)：每条上位机指令对应
/// 第一次回复(ACK) 与 第二次回复(INF 完成 / ABS 异常，由 Type 决定发哪条)。
/// Hits/Last 为运行时统计列，不落盘。
/// </summary>
public sealed class ResponseRow : INotifyPropertyChanged
{
    private string _cmd = "";
    private string _note = "";
    private string _ack = "";
    private string _inf = "";
    private string _abs = "";
    private string _type = "INF";
    private int _hits;
    private string _last = "";

    /// <summary>上位机发送指令, 如 "MOV:CLOAD"。</summary>
    public string Cmd { get => _cmd; set => SetProperty(ref _cmd, value); }

    /// <summary>说明。</summary>
    public string Note { get => _note; set => SetProperty(ref _note, value); }

    /// <summary>第一次回复(ACK) 体。</summary>
    public string Ack { get => _ack; set => SetProperty(ref _ack, value); }

    /// <summary>第二次回复-INF 体, 多帧用 '|' 分隔。</summary>
    public string Inf { get => _inf; set => SetProperty(ref _inf, value); }

    /// <summary>第二次回复-ABS 体。</summary>
    public string Abs { get => _abs; set => SetProperty(ref _abs, value); }

    /// <summary>第二次实际回复哪条: INF / ABS。</summary>
    public string Type { get => _type; set => SetProperty(ref _type, value); }

    /// <summary>运行时统计: 收到次数 (不落盘)。</summary>
    [JsonIgnore]
    public int Hits { get => _hits; private set => SetProperty(ref _hits, value); }

    /// <summary>运行时统计: 最近收到时刻 (不落盘)。</summary>
    [JsonIgnore]
    public string Last { get => _last; private set => SetProperty(ref _last, value); }

    /// <summary>命中一次: 计数 +1、刷新时刻。串口线程也会调, 由 UI 侧 Dispatcher 封送。</summary>
    public void Bump()
    {
        Hits++;
        Last = DateTime.Now.ToString("HH:mm:ss");
    }

    /// <summary>清零命中统计 ([清零计数] 按钮)。</summary>
    public void ResetHits()
    {
        Hits = 0;
        Last = "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));
    }
}

/// <summary>
/// 应答表配置的读写。文件 responses.json 与 config.json 同目录（--data 指定的数据目录），
/// 既可在界面里改后点"保存配置"，也可直接用文本编辑器改文件再"重新加载"。
/// </summary>
public static class ResponseStore
{
    /// <summary>独立运行时的默认路径 (exe 旁)。多实例请用 --data 指定, 各实例独立。</summary>
    public static string DefaultPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "responses.json");

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
        // 让中文(说明)与符号原样写出, 方便手工编辑
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static bool Exists(string path) => File.Exists(path);

    public static List<ResponseRow> Load(string path)
    {
        string json = File.ReadAllText(path);
        var list = JsonSerializer.Deserialize<List<ResponseRow>>(json);
        return list ?? new List<ResponseRow>();
    }

    public static void Save(string path, List<ResponseRow> rows)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(rows, SaveOptions));
    }
}
