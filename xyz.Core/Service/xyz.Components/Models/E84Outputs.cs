namespace xyz.Components.Models;

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
