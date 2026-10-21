namespace xyz.Drivers.Aligner;

/// <summary>
/// 对准器一条指令的结果（跟品牌无关）
/// </summary>
public sealed class AlignerResponse
{
    /// <summary>
    /// 做成了没有
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 没做成的原因（设备报错、超时、作废），做成为空
    /// </summary>
    public string Error { get; init; } = string.Empty;
}
