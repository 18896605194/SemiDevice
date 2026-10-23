namespace xyz.Modules;

/// <summary>
/// 机械手替 LoadPort 扫片的步骤
/// </summary>
public enum RobotMapStep
{
    /// <summary>
    /// 排队等机械手：轮到这一条、没在搬片、手上全空
    /// </summary>
    WaitRobot,

    /// <summary>
    /// 机械手在扫
    /// </summary>
    WaitMap,
}
