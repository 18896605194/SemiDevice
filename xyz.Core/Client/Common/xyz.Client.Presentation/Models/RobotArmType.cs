namespace xyz.Client.Presentation.Models;

/// <summary>
/// 机械手手臂结构。
/// </summary>
public enum RobotArmType
{
    /// <summary>
    /// 直伸直出：手臂沿直线导轨伸缩。
    /// </summary>
    Linear,

    /// <summary>
    /// 蛙式：左右两组连杆带动腕部沿直线伸缩。
    /// </summary>
    FrogLeg,
}
