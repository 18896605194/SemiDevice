using System.Globalization;

namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// Speed（@Speed&lt;1-100&gt;;）：设置全局速度百分比。
/// </summary>
public sealed class RejeSetSpeedCommand : RejeCommand
{
    private readonly string _wire;

    public RejeSetSpeedCommand(IRobotDriver driver, int speed) : base(driver)
    {
        if (speed is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "速度须为 1-100。");
        }

        _wire = "Speed" + speed.ToString(CultureInfo.InvariantCulture);
    }

    protected override string Wire => _wire;
}
