using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 日志下拉框的显示模型，由 <see cref="LogDto"/> 映射，字段同名对齐。
/// </summary>
public class LogModel
{
    /// <summary>产生时刻。</summary>
    public DateTime Time { get; init; }

    /// <summary>级别：Debug / Info / Warn / Error。</summary>
    public string Level { get; init; } = "Info";

    /// <summary>来源模块（如 LoadPort1），可为空。</summary>
    public string Module { get; init; } = string.Empty;

    /// <summary>日志正文。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>来源：Server / Client。</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>下拉列表里的时刻文本。</summary>
    public string TimeText => Time.ToString("HH:mm:ss.fff");

    /// <summary>下拉列表里的来源标记：后端日志加 [S]，客户端加 [C]。</summary>
    public string SourceText => string.IsNullOrEmpty(Source) ? string.Empty : $"[{Source[..1].ToUpperInvariant()}]";

    public static LogModel From(LogDto dto)
    {
        return new LogModel
        {
            Time = dto.Time,
            Level = dto.Level,
            Module = dto.Module,
            Message = dto.Message,
            Source = dto.Source,
        };
    }
}
