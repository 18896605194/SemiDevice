using xyz.Elements.Enums;

namespace xyz.Modules.Enums;

public class RobotState : ModuleState
{
    public const int Homing = 200;

    public const int Picking = 210;

    /// <summary>
    /// 分段取片伸着停在站里，等站点把片交到手上；只收取片缩臂和急停
    /// </summary>
    public const int PickExtended = 211;

    public const int Placing = 220;

    /// <summary>
    /// 分段放片伸着停在站里，等站点把片接走；只收放片缩臂和急停
    /// </summary>
    public const int PlaceExtended = 221;

    /// <summary>
    /// 扫片中（替 LoadPort 扫料盒每一槽有没有片）
    /// </summary>
    public const int Mapping = 230;
}

/// <summary>
/// 机械手的动作名（状态表、动作入口按它查）；公共动作继承自 <see cref="ModuleAction"/>
/// </summary>
public class RobotAction : ModuleAction
{
    public const string Pick = "Pick";

    public const string Place = "Place";

    public const string PowerOn = "PowerOn";

    public const string PowerOff = "PowerOff";

    public const string ExtendPick = "ExtendPick";

    public const string RetractPick = "RetractPick";

    public const string ExtendPlace = "ExtendPlace";

    public const string RetractPlace = "RetractPlace";

    public const string Map = "Map";
}
