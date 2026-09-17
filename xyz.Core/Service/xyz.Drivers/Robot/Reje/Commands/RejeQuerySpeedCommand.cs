using System.Globalization;

namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// QSpeed（@QSpeed;）：查询全局速度百分比，内容段（允许小数）解析成 Response.Speed。
/// </summary>
public sealed class RejeQuerySpeedCommand : RejeCommand
{
    public RejeQuerySpeedCommand(IRobotDriver driver) : base(driver)
    {
    }

    protected override string Wire => "QSpeed";

    protected override RobotResponse BuildResponse(string content)
    {
        if (!double.TryParse(content.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double speed)
            || double.IsNaN(speed) || double.IsInfinity(speed))
        {
            return new RobotResponse
            {
                IsSuccess = false,
                Error = $"QSpeed 内容无法识别: {content}",
                Content = content,
            };
        }

        return new RobotResponse
        {
            IsSuccess = true,
            Content = content,
            Speed = speed,
        };
    }
}
