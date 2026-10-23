using System.Globalization;

namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// QSpeed（@QSpeed;）：查全局速度百分比（允许小数）
/// </summary>
public sealed class RejeQuerySpeedCommand : RejeCommand
{
    protected override string Wire => "QSpeed";

    /// <summary>
    /// 查到的速度百分比
    /// </summary>
    public double? Speed { get; private set; }

    protected override bool Handle(RejeMessage message)
    {
        if (!string.Equals(message.Name, Wire, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (message.Code != RejeProtocol.SuccessCode)
        {
            Fail($"{message.Code}#{message.Content}");
            return true;
        }

        if (!double.TryParse(message.Content.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double speed)
            || double.IsNaN(speed) || double.IsInfinity(speed))
        {
            Fail($"QSpeed 内容无法识别: {message.Content}");
            return true;
        }

        Speed = speed;
        Complete();
        return true;
    }
}
