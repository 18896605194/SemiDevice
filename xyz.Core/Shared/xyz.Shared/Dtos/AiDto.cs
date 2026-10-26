namespace xyz.Shared.Dtos;

/// <summary>
/// AI 的一次回答：正文，加上它看了哪些数据（界面列出来给人核对）
/// </summary>
public class AiAnswerDto
{
    /// <summary>
    /// 回答正文
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 哪个模型答的
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 带给模型的日志条数
    /// </summary>
    public int LogCount { get; set; }

    /// <summary>
    /// 带给模型的信号名
    /// </summary>
    public List<string> Signals { get; set; } = [];

    /// <summary>
    /// 从发起到拿到回答用了多久（含排队）
    /// </summary>
    public long ElapsedMs { get; set; }
}

/// <summary>
/// 这台机的 AI 配了没有、用的哪个模型
/// </summary>
public class AiStatusDto
{
    /// <summary>
    /// 模型名
    /// </summary>
    public string Model { get; set; } = string.Empty;
}
