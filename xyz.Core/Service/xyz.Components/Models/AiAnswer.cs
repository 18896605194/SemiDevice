namespace xyz.Components.Models;

public sealed class AiAnswer
{
    /// <summary>
    /// 回答正文
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 哪个模型答的
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// 带给模型的日志条数
    /// </summary>
    public int LogCount { get; init; }

    /// <summary>
    /// 带给模型的信号名
    /// </summary>
    public IReadOnlyList<string> Signals { get; init; } = [];

    /// <summary>
    /// 从发起到拿到回答用了多久（含排队）
    /// </summary>
    public long ElapsedMs { get; init; }
}
