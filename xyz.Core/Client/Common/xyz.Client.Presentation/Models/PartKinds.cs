namespace xyz.Client.Presentation.Models;

/// <summary>
/// 界面认识的部件种类（后端组件类上 [PartKind] 的种类名，跟部件推送 PartDto.Kind 一致）和三维图要认的组件类名。
/// 界面不认识的种类只收数据、不显示。
/// </summary>
public static class PartKinds
{
    /// <summary>运动轴：手动页一根轴一个页签。</summary>
    public const string Axis = "Axis";

    /// <summary>双作用气缸：手动页气缸表一行。</summary>
    public const string TwoState = "TwoState";

    /// <summary>单线圈阀（含喷嘴）：手动页现在不放，三维图画出液。</summary>
    public const string OneState = "OneState";

    /// <summary>摆臂轴的组件类名：三维图按它搭摆臂。</summary>
    public const string ArmAxisType = "ArmAxisComponent";

    /// <summary>旋转电机的组件类名：三维图按它转盘面。</summary>
    public const string SpinMotorType = "SpinMotorComponent";
}
