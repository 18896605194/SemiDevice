namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// QEnable（@QEnable;）：查伺服上没上使能，内容段 YES / NO
/// </summary>
public sealed class RejeQueryEnableCommand : RejeCommand
{
    protected override string Wire => "QEnable";

    /// <summary>
    /// 查到的伺服使能
    /// </summary>
    public bool? ServoOn { get; private set; }

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

        switch (message.Content.Trim().ToUpperInvariant())
        {
            case "YES":
                ServoOn = true;
                Complete();
                return true;

            case "NO":
                ServoOn = false;
                Complete();
                return true;

            default:
                Fail($"QEnable 内容无法识别: {message.Content}");
                return true;
        }
    }
}
