namespace xyz.Modules.Enums;

/// <summary>
/// Robot 状态码；公共状态继承自 <see cref="ModuleState"/>。
/// </summary>
public class RobotState : ModuleState
{
    public const int Homing = 200;

    public const int Picking = 210;

    public const int Placing = 220;
}

public enum RobotAction
{
    Home,
    Reset,
    Abort,
    Pick,
    Place,
    PowerOn,
    PowerOff
}
