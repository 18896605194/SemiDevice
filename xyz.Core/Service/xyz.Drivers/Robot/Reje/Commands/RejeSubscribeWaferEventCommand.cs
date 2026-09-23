namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// SubWafer1（@SubWafer1;）：订阅手指在位推送；订阅后手指有无片变化时设备主动推 SubWaferEx。
/// </summary>
public sealed class RejeSubscribeWaferEventCommand : RejeCommand
{
    public RejeSubscribeWaferEventCommand(IRobotDriver driver) : base(driver)
    {
    }

    protected override string Wire => "SubWafer1";
}
