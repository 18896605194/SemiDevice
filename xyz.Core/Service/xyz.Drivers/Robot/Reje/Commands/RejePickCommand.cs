namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// Pick（@GXYYZZ;）：用手指 X 从工位 YY 的层 ZZ 取片（工位、层为十六进制）。
/// </summary>
public sealed class RejePickCommand : RejeCommand
{
    private readonly string _wire;

    public RejePickCommand(RobotDriverBase driver, int arm, int station, int slot) : base(driver)
    {
        _wire = "G" + RejeProtocol.EncodeMotionArgs(arm, station, slot);
    }

    protected override string Wire => _wire;

    protected override IReadOnlyList<string> FailureEchoes => ["G"];

    public override bool IsMotion => true;
}
