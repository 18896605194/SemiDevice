using System.Globalization;

namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// &lt;轴&gt;Pos（如 @XPos;、@Arm1Pos;）：查询指定轴当前坐标，内容段解析成 Response.Position。
/// </summary>
public sealed class RejeQueryAxisPosCommand : RejeCommand
{
    private readonly string _wire;

    public RejeQueryAxisPosCommand(IRobotDriver driver, string axis) : base(driver)
    {
        _wire = axis + "Pos";
    }

    protected override string Wire => _wire;

    protected override RobotResponse BuildResponse(string content)
    {
        if (!double.TryParse(content.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double position))
        {
            return new RobotResponse
            {
                IsSuccess = false,
                Error = $"{_wire} 内容无法识别: {content}",
                Content = content,
            };
        }

        return new RobotResponse
        {
            IsSuccess = true,
            Content = content,
            Position = position,
        };
    }
}
