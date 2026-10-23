namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// Error（@Error;）：查控制器当前报错。没错回 00000000；有错回八位报警码 + 内容——这是查到的结果、不是指令失败
/// </summary>
public sealed class RejeQueryErrorCommand : RejeCommand
{
    protected override string Wire => "Error";

    /// <summary>
    /// 查到的报错（"错误码#内容"），没有报错为 null
    /// </summary>
    public string? DeviceError { get; private set; }

    protected override bool Handle(RejeMessage message)
    {
        if (!string.Equals(message.Name, Wire, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 错误码不是 00000000 也算查成了：那就是控制器当前的报错
        if (message.Code != RejeProtocol.SuccessCode)
        {
            DeviceError = $"{message.Code}#{message.Content}";
            Complete();
            return true;
        }

        DeviceError = null;
        Complete();
        return true;
    }
}
