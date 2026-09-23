namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// Place（@PXYYZZ;）：用手指 X 向工位 YY 的层 ZZ 放片（工位、层为十六进制）。
/// </summary>
public sealed class RejePlaceCommand : RejeCommand
{
    private readonly string _wire;

    public RejePlaceCommand(IRobotDriver driver, int arm, int station, int slot) : base(driver)
    {
        _wire = "P" + RejeProtocol.EncodeMotionArgs(arm, station, slot);
    }

    protected override string Wire => _wire;

    protected override IReadOnlyList<string> FailureEchoes => ["P"];

    public override bool IsMotion => true;
}
