using xyz.Components.Enums;

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

public enum RobotAction
{
    Home,
    Reset,
    Abort,
    Pick,
    Place,
    PowerOn,
    PowerOff,
    ExtendPick,
    RetractPick,
    ExtendPlace,
    RetractPlace,
    Map
}
