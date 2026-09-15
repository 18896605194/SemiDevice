namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// PowerOff（@PowerOff;）：伺服下使能，仅自动模式有效。
/// </summary>
public sealed class RejePowerOffCommand : RejeCommand
{
    public RejePowerOffCommand(RobotDriverBase driver) : base(driver)
    {
    }

    protected override string Wire => "PowerOff";
}
