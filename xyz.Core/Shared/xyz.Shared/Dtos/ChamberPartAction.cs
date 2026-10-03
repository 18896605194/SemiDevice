namespace xyz.Shared.Dtos;

/// <summary>
/// 腔体部件的手动动作。按部件种类给：气缸开 / 关（门开关、Bowl 和 Lift 升降，开侧 = 门开 / 升），
/// 喷嘴出液 / 停液，摆臂回零 / 去工艺位，旋转电机转 / 停。
/// </summary>
public enum ChamberPartAction
{
    /// <summary>气缸到开侧：门开、Bowl 升、Lift 升。</summary>
    Open = 0,

    /// <summary>气缸到关侧：门关、Bowl 降、Lift 降。</summary>
    Close = 1,

    /// <summary>喷嘴出液（阀通电）。</summary>
    On = 2,

    /// <summary>喷嘴停液（阀断电）。</summary>
    Off = 3,

    /// <summary>摆臂回零。</summary>
    Home = 4,

    /// <summary>摆臂去工艺位：轴走到 EC Center（Wafer 中心标定）。</summary>
    Center = 5,

    /// <summary>旋转电机按 EC ManualSpeed 转起来。</summary>
    Start = 6,

    /// <summary>旋转电机停。</summary>
    Stop = 7,
}
