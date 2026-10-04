namespace xyz.Client.Presentation.Models;

/// <summary>
/// 部件推送里实时数据的名字：就是后端组件上标了 [LiveValue] 的属性名，改了属性名这里跟着改。
/// </summary>
public static class PartValueNames
{
    /// <summary>轴：PLC 状态有效（false 时位置、速度是断线前的旧值）。</summary>
    public const string HasPlcData = "HasPlcData";

    /// <summary>轴：实际位置。</summary>
    public const string CurrentPosition = "CurrentPosition";

    /// <summary>轴：实际速度（旋转电机正负是转向）。</summary>
    public const string CurrentSpeed = "CurrentSpeed";

    /// <summary>轴：伺服使能（伺服就绪）。</summary>
    public const string IsServoOn = "IsServoOn";

    /// <summary>轴：已回零。</summary>
    public const string IsHomed = "IsHomed";

    /// <summary>轴：运动中（PLC 忙）。</summary>
    public const string IsBusy = "IsBusy";

    /// <summary>轴：已到位。</summary>
    public const string IsInPosition = "IsInPosition";

    /// <summary>轴：驱动器报错（故障）。</summary>
    public const string IsError = "IsError";

    /// <summary>摆臂：摆到哪（0 = Home，1 = 工艺位）。</summary>
    public const string Reach = "Reach";

    /// <summary>摆臂：第一个边缘在 Reach 上的位置（0 = 没示教）。</summary>
    public const string EdgeReach = "EdgeReach";

    /// <summary>旋转电机：在转。</summary>
    public const string IsSpinning = "IsSpinning";

    /// <summary>双作用气缸：在哪一侧（Opened / Closed / Unknown）。</summary>
    public const string Position = "Position";

    /// <summary>阀、喷嘴：通着（在出液）。</summary>
    public const string IsOn = "IsOn";
}
