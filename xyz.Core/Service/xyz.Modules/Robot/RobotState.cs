namespace xyz.Modules.Enums;

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
