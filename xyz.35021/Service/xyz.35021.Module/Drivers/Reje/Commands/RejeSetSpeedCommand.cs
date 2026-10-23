using System.Globalization;

namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// Speed（@Speed&lt;1-100&gt;;）：设全局速度百分比
/// </summary>
public sealed class RejeSetSpeedCommand : RejeCommand
{
    private readonly string _wire;

    public RejeSetSpeedCommand(int speed)
    {
        if (speed < 1 || speed > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "速度须为 1-100。");
        }

        _wire = "Speed" + speed.ToString(CultureInfo.InvariantCulture);
    }

    protected override string Wire => _wire;

    protected override bool Handle(RejeMessage message)
    {
        if (!string.Equals(message.Name, _wire, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (message.Code != RejeProtocol.SuccessCode)
        {
            Fail($"{message.Code}#{message.Content}");
            return true;
        }

        Complete();
        return true;
    }
}
