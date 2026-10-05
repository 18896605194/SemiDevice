namespace xyz.Modules;

/// <summary>
/// 一张搬运单：把哪片从哪儿搬到哪儿、谁下的。手动单、Job 下的单、恢复用的单都是它，区别只在 <see cref="Origin"/> 和 <see cref="Owner"/>。
/// 站点名就是模块名（sc.xml 原样），槽号从 1 开始。
/// </summary>
public sealed record TransferRequest
{
    /// <summary>谁下的单：手动、自动（Job）、恢复。</summary>
    public TransferOrigin Origin { get; init; } = TransferOrigin.Manual;

    /// <summary>下单的 Job（PJ 名）；手动单为空。Job 只能搬自己的片，中止时也按它撤单。</summary>
    public string? Owner { get; init; }

    /// <summary>
    /// 要搬的那一片（晶圆账的内部标识）；空 = 源槽上现在那一片。
    /// Job 下单必给：槽上的片被人换过时宁可拒单，也不能搬错片。
    /// </summary>
    public Guid? WaferId { get; init; }

    /// <summary>源站点（模块名）。</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>源槽号。</summary>
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
