namespace xyz.Modules.Enums;


public class ChamberState : TransferModuleState
{
    public const int Homing = 100;

    public const int Processing = 110;

    public const int Manual = 120;
}

public enum ChamberAction
{
    Home,
    Process,
    Reset,
    Abort,

    /// <summary>设备手动动作。</summary>
    Manual,
}

/// <summary>
/// 发起设备手动动作的结果。
/// </summary>
public enum ChamberDeviceActionResult
{
    /// <summary>普通动作已经发起，腔体进"手动中"等它做完。</summary>
    Started,

    /// <summary>按住类动作（点动）已经发起，之后按住期间续、松手发松手动作。</summary>
    Holding,

    /// <summary>停止已经发出去（不挂操作、不等结果）。</summary>
    Sent,

    /// <summary>腔体下没有这根轴 / 这个气缸。</summary>
    NotFound,

    /// <summary>参数不对：不是有限数、速度是负的、点动速度或步距是 0。</summary>
    InvalidArgs,

    /// <summary>状态不允许，或已有动作在途。</summary>
    Rejected,

    /// <summary>指令没发出去（PLC 没连、轴没回零 / 没使能 / 正忙……）。</summary>
    CommandRejected,
}
