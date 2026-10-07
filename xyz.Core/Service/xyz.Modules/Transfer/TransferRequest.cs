namespace xyz.Modules;

/// <summary>
/// 搬运操作的输入参数：哪片从哪儿搬到哪儿。手动、Job、人工恢复共用，区别在 <see cref="Origin"/> 和 <see cref="Owner"/>。
/// 站点名就是模块名（sc.xml 原样），槽号从 1 开始。
/// </summary>
public sealed record TransferRequest
{
    /// <summary>调用来源：手动、自动（Job）、恢复。</summary>
    public TransferOrigin Origin { get; init; } = TransferOrigin.Manual;

    /// <summary>任务所属 Job（PJ 名）；手动传片为空。Job 只能搬自己的片，中止时按归属停止操作。</summary>
    public string? Owner { get; init; }

    /// <summary>
    /// 要搬的那一片（晶圆账的内部标识）；空 = 源槽上现在那一片。
    /// Job 执行任务时必给，用来校验源槽上仍是这片晶圆。
    /// </summary>
    public Guid? WaferId { get; init; }

    /// <summary>
    /// 源站点（模块名）。片已经在机械手手上（重启前搬到一半、手动取了没放）时写机械手名：只放片，源槽写拿着它的手指号。
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>源槽号（源是机械手时是手指号）。</summary>
    public int SourceSlot { get; init; }

    /// <summary>目标站点（模块名）。</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>目标槽号。</summary>
    public int TargetSlot { get; init; }

    /// <summary>用哪台机械手；空 = 挑第一台两个站点都到得了的（按 sc.xml 先后）。</summary>
    public string? Robot { get; init; }

    /// <summary>用哪只手；0 = 挑一只空着、两个站点都许用的（手指号从小到大）。</summary>
    public int Arm { get; init; }
}
