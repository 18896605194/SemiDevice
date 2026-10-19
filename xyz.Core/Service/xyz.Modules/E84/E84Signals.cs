namespace xyz.Modules;

/// <summary>
/// 和天车交互的信号
/// </summary>
public readonly record struct E84Inputs
{
    public bool Valid { get; init; }

    public bool Cs0 { get; init; }

    public bool Cs1 { get; init; }

    public bool AmAvbl { get; init; }

    public bool TrReq { get; init; }

    public bool Busy { get; init; }

    public bool Compt { get; init; }

    public bool Cont { get; init; }

    public bool LightCurtain { get; init; }
}

/// <summary>
/// 给天车 输出信号。
/// </summary>
public readonly record struct E84Outputs
{
    public bool LReq { get; init; }

    public bool UReq { get; init; }

    public bool Ready { get; init; }

    public bool HoAvbl { get; init; }

    public bool Es { get; init; }
}
