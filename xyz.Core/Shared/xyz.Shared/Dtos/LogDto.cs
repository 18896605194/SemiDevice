namespace xyz.Shared.Dtos;

/// <summary>
/// 日志契约
/// </summary>
public class LogDto
{

    public const string EventToken = "Log";

    public DateTime Time { get; set; } = DateTime.Now;

    public string Level { get; set; } = "Info";

    public string Module { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;
}
