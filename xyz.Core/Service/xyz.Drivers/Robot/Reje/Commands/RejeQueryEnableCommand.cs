namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// QEnable（@QEnable;）：查询伺服是否上使能，内容段 YES/NO 解析成 Response.ServoOn。
/// </summary>
public sealed class RejeQueryEnableCommand : RejeCommand
{
    public RejeQueryEnableCommand(RobotDriverBase driver) : base(driver)
    {
    }

    protected override string Wire => "QEnable";

    protected override RobotResponse BuildResponse(string content)
    {
        string text = content.Trim();
        bool? servoOn = text.ToUpperInvariant() switch
        {
            "YES" => true,
            "NO" => false,
            _ => null,
        };

        if (servoOn is null)
        {
            return new RobotResponse
            {
                IsSuccess = false,
                Error = $"QEnable 内容无法识别: {content}",
                Content = content,
            };
        }

        return new RobotResponse
        {
            IsSuccess = true,
            Content = content,
            ServoOn = servoOn,
        };
    }
}
