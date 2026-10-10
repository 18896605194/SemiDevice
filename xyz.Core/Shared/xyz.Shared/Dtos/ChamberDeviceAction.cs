namespace xyz.Shared.Dtos;

/// <summary>
/// 腔体对设备发的动作（手动页的按钮，和回零、工艺里一步一步发的）。设备动作的错误码参数里带它的名字，界面按语言包 device.action.* 换成叫法。
/// </summary>
public enum ChamberDeviceAction
{
    /// <summary>轴回零。</summary>
    Home,

    /// <summary>轴走到绝对位置。</summary>
    Move,

    /// <summary>轴走一段（步进）。</summary>
    Step,

    /// <summary>轴点动（按住类）。</summary>
    Jog,

    /// <summary>轴停止。</summary>
    Stop,

    /// <summary>轴驱动器复位清错。</summary>
    Reset,

    /// <summary>卡盘按转速转。</summary>
    Spin,

    /// <summary>气缸升（开侧：门开、Bowl 升、Lift 升）。</summary>
    Up,

    /// <summary>气缸降（关侧）。</summary>
    Down,

    /// <summary>喷嘴开阀出液。</summary>
    ValveOn,

    /// <summary>喷嘴停液（关阀、流量设定清零）。</summary>
    ValveOff,

    /// <summary>喷嘴流量设定。</summary>
    Flow,
}
