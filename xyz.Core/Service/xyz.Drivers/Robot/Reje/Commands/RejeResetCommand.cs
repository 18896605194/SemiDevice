namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// Reset（@Reset;）：清除控制器报错。
/// </summary>
public sealed class RejeResetCommand : RejeCommand
{
    public RejeResetCommand(IRobotDriver driver) : base(driver)
    {
    }

    protected override string Wire => "Reset";
}
