namespace xyz.Shared.Dtos;

/// <summary>
/// 机械手转台方位（俯视，上北下南、左西右东），枚举值即转角度数，顺时针为正。
/// sc.xml 机械手站点表、状态推送与界面 Robot 控件共用。
/// </summary>
public enum RobotDirection
{
    /// <summary>
    /// 北，0°。
    /// </summary>
    North = 0,

    /// <summary>
    /// 东，90°。
    /// </summary>
    East = 90,

    /// <summary>
    /// 南，180°。
    /// </summary>
    South = 180,

    /// <summary>
    /// 西，270°。
    /// </summary>
    West = 270,
}
