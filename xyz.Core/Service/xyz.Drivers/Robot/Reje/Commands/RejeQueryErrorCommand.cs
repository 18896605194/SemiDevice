namespace xyz.Drivers.Robot.Reje.Commands;

/// <summary>
/// Error（@Error;）：查询控制器当前报错。无错回 00000000；有错回八位报警码 + 内容——
/// 这是查询结果而非指令失败，解析成 Response.DeviceError（"错误码#内容"，无错为 null）。
/// </summary>
public sealed class RejeQueryErrorCommand : RejeCommand
{
    public RejeQueryErrorCommand(RobotDriverBase driver) : base(driver)
    {
    }

    protected override string Wire => "Error";

    protected override RobotResponse BuildResponse(string content)
    {
        return new RobotResponse
        {
            IsSuccess = true,
            Content = content,
            DeviceError = null,
        };
    }

    protected override RobotResponse BuildFailure(string code, string content)
    {
        return new RobotResponse
        {
            IsSuccess = true,
            Content = content,
            DeviceError = $"{code}#{content}",
        };
    }
}
