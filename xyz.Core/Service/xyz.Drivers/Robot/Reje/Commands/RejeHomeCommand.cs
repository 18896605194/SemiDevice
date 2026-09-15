namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// Home（@Home;）：全轴回原点。
/// </summary>
public sealed class RejeHomeCommand : RejeCommand
{
    public RejeHomeCommand(RobotDriverBase driver) : base(driver)
    {
    }

    protected override string Wire => "Home";

    public override bool IsMotion => true;
}
