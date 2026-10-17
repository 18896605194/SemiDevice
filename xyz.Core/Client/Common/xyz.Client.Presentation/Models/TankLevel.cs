namespace xyz.Client.Presentation.Models;

/// <summary>
/// 槽的液位档，跟后端 TankLevel 一一对应（TankDto.Level 按这个顺序转）
/// </summary>
public enum TankLevel
{
    /// <summary>
    /// 信号读不到或对不上
    /// </summary>
    Unknown,

    /// <summary>
    /// 低于 LL
    /// </summary>
    LL,

    /// <summary>
    /// LL 到了、L 没到
    /// </summary>
    L,

    /// <summary>
    /// L 到了、H 没到
    /// </summary>
    Normal,

    /// <summary>
    /// H 到了、HH 没到
    /// </summary>
    H,

    /// <summary>
    /// HH 到了
    /// </summary>
    HH,

    /// <summary>
    /// Overfill 亮了，已经满出来
    /// </summary>
    Overfill
}
