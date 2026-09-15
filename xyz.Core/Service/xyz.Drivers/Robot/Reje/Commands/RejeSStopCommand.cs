namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// Sstop（@Sstop;）：急停，打断在途运动；被打断的运动指令设备不再回结果，由驱动在本指令成功后终结。
/// </summary>
public sealed class RejeSStopCommand : RejeCommand
{
    public RejeSStopCommand(RobotDriverBase driver) : base(driver)
    {
    }

    protected override string Wire => "Sstop";
}
