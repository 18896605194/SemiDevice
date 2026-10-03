namespace xyz.Modules.Enums;


public class ChamberState : TransferModuleState
{
    public const int Homing = 100;

    /// <summary>工艺中。</summary>
    public const int Processing = 110;

    /// <summary>部件手动动作中（门、Bowl、Lift、喷嘴、摆臂、旋转电机）；做完回到发起前的状态。</summary>
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
/// 发起部件手动动作的结果：找不到部件、部件不支持、状态不允许（或已有动作在途）、指令没发出去，或已经发起。
/// </summary>
public enum ChamberPartActionResult
{
    Started,
    NotFound,
    Unsupported,
    Rejected,
    CommandRejected,
}
