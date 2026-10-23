namespace xyz._35021.Module.Drivers.Reje;

/// <summary>
/// 锐洁的一条回复 / 推送（解码器解好的）：&gt;错误码#内容@回显名
/// </summary>
public sealed class RejeMessage
{
    /// <summary>
    /// 错误码，00000000 是成功
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// 内容段
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 回显名：指令靠它对号，推送靠它认是哪一种
    /// </summary>
    public string Name { get; init; } = string.Empty;
}
