namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// PowerOn（@PowerOn;）：伺服上使能，仅自动模式有效。
/// </summary>
public sealed class RejePowerOnCommand : RejeCommand
{
    public RejePowerOnCommand(IRobotDriver driver) : base(driver)
    {
    }

    protected override string Wire => "PowerOn";
}
