namespace xyz.Modules.Enums;


public class ChamberState : TransferModuleState
{
    public const int Homing = 100;

    /// <summary>工艺中。</summary>
    public const int Processing = 110;

    /// <summary>部件手动动作中（轴、气缸……手动页上的部件动作，含点动按住期间）；做完回到发起前的状态。</summary>
    public const int Manual = 120;
}

public enum ChamberAction
{
    Home,
    Process,
    Reset,
    Abort,

    /// <summary>部件手动动作。</summary>
    Manual,
}

/// <summary>
/// 发起部件手动动作的结果。
/// </summary>
public enum ChamberPartActionResult
{
    /// <summary>普通动作已经发起，腔体进"手动中"等它做完。</summary>
    Started,

    /// <summary>按住类动作（点动）已经发起，之后按住期间续、松手发松手动作。</summary>
    Holding,

    /// <summary>停止类动作已经发出去（不挂操作、不等结果）。</summary>
    Sent,

    /// <summary>腔体下没有这个部件。</summary>
    NotFound,

    /// <summary>部件没有这个手动动作。</summary>
    Unsupported,

    /// <summary>参数个数或格式不对。</summary>
    InvalidArgs,

    /// <summary>状态不允许，或已有动作在途。</summary>
    Rejected,

    /// <summary>指令没发出去（PLC 没连、轴没回零 / 没使能 / 正忙……）。</summary>
    CommandRejected,
}
