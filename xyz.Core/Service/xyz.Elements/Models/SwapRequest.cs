using xyz.Elements.Enums;

namespace xyz.Elements.Models;

/// <summary>
/// 同腔交换请求：同一台机械手在同一个站点的同一个槽，先用取片手取走做完的旧片，再用放片手放进新片
/// </summary>
public sealed record SwapRequest
{
    /// <summary>
    /// 谁发起的
    /// </summary>
    public TransferOrigin Origin { get; init; } = TransferOrigin.Manual;

    /// <summary>
    /// 哪台机械手
    /// </summary>
    public string Robot { get; init; } = string.Empty;

    /// <summary>
    /// 在哪个站点交换
    /// </summary>
    public string Station { get; init; } = string.Empty;

    /// <summary>
    /// 站点的哪个槽（旧片在这儿，新片也放这儿）
    /// </summary>
    public int Slot { get; init; }

    /// <summary>
    /// 取旧片用的手（要空着）
    /// </summary>
    public int PickArm { get; init; }

    /// <summary>
    /// 放新片用的手（拿着新片）
    /// </summary>
    public int PlaceArm { get; init; }

    /// <summary>
    /// 站里预期的旧片（晶圆账内部标识）
    /// </summary>
    public Guid PickWafer { get; init; }

    /// <summary>
    /// 放片手上预期的新片（晶圆账内部标识）
    /// </summary>
    public Guid PlaceWafer { get; init; }

    /// <summary>
    /// 旧片归哪个 Job；手动为空
    /// </summary>
    public string? PickOwner { get; init; }

    /// <summary>
    /// 新片归哪个 Job；手动为空
    /// </summary>
    public string? PlaceOwner { get; init; }
}
