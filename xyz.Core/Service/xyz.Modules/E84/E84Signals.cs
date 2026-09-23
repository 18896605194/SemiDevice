namespace xyz.Modules;

/// <summary>
/// 搬运车（主动方）给本端口的 E84 输入信号，一个扫描周期读一次。
/// </summary>
public readonly record struct E84Inputs
{
    /// <summary>VALID：搬运车这次交接的信号有效。</summary>
    public bool Valid { get; init; }

    /// <summary>CS_0：搬运车选中本端口交接。</summary>
    public bool Cs0 { get; init; }

    /// <summary>CS_1：第二载具位（双位端口用），本流程不看。</summary>
    public bool Cs1 { get; init; }

    /// <summary>AM_AVBL：搬运车一侧可交接，本流程不看。</summary>
    public bool AmAvbl { get; init; }

    /// <summary>TR_REQ：搬运车请求交接。</summary>
    public bool TrReq { get; init; }

    /// <summary>BUSY：搬运车正在进出料。</summary>
    public bool Busy { get; init; }

    /// <summary>COMPT：搬运车交接完成。</summary>
    public bool Compt { get; init; }

    /// <summary>CONT：连续交接，本流程不看。</summary>
    public bool Cont { get; init; }

    /// <summary>光幕输入的原始电平；哪个电平算被挡由 SC LightCurtainReverse 定，没配光幕时不看。</summary>
    public bool LightCurtain { get; init; }
}

/// <summary>
/// 本端口给搬运车的 E84 输出信号。
/// </summary>
public readonly record struct E84Outputs
{
    /// <summary>L_REQ：请求送盒进来。</summary>
    public bool LReq { get; init; }

    /// <summary>U_REQ：请求把盒取走。</summary>
    public bool UReq { get; init; }

    /// <summary>READY：准备好交接。</summary>
    public bool Ready { get; init; }

    /// <summary>HO_AVBL：本端口可交接。</summary>
    public bool HoAvbl { get; init; }

    /// <summary>ES：ON 为正常，OFF 让搬运车急停。</summary>
    public bool Es { get; init; }
}
