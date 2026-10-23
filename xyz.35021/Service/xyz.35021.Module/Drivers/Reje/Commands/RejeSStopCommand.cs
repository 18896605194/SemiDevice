namespace xyz._35021.Module.Drivers.Reje.Commands;

/// <summary>
/// Sstop（@Sstop;）：急停；被打断的运动设备不再回结果，平台中止动作时已经把它们作废
/// </summary>
public sealed class RejeSStopCommand : RejeCommand
{
    protected override string Wire => "Sstop";

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

        Complete();
        return true;
    }
}
