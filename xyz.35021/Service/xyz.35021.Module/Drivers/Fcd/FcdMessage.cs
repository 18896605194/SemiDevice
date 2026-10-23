namespace xyz._35021.Module.Drivers.Fcd;

/// <summary>
/// FCD 的一条回复 / 事件（解码器解好的）：类型:名字/数据，比如 ACK:MAPDT/PPE…、INF:PODON
/// </summary>
public sealed class FcdMessage
{
    /// <summary>
    /// 回复类型（大写）：ACK / NAK / INF / ABS
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// 指令名或事件名，指令靠它对号
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 名字后面 '/' 之后的数据段，没有为空
    /// </summary>
    public string Data { get; init; } = string.Empty;
}
