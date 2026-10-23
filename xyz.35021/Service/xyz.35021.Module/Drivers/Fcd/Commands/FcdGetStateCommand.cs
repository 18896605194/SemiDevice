using xyz.Drivers.Loadport;

namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// 查系统状态（GET:STATE）：64 字符状态串随 ACK 回、ACK 就收尾，解成 LoadPortStatus
/// </summary>
public sealed class FcdGetStateCommand : FcdCommand
{
    private const int StateLength = 64;

    protected override string Name => "STATE";

    /// <summary>
    /// 查到的设备状态
    /// </summary>
    public LoadPortStatus? Status { get; private set; }

    public override string BuildFrame()
    {
        return $"{FcdProtocol.Get}:STATE";
    }

    protected override bool Handle(FcdMessage message)
    {
        if (!string.Equals(message.Name, Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        switch (message.Type)
        {
            case FcdProtocol.Ack:
            case FcdProtocol.Inf:
                // 查询类：状态串随 ACK 回，后面没有 INF；万一回的是 INF 也一样收
                ReadState(message.Data);
                return true;

            case FcdProtocol.Abs:
            case FcdProtocol.Nak:
                Fail($"{message.Type}:{Name}/{message.Data}");
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 状态串解成 LoadPortStatus：第 1/2/8 位是在位、放好、报警，第 43/44 位是门开、门关；其余位待协议手册确认
    /// </summary>
    private void ReadState(string data)
    {
        if (data.Length < StateLength)
        {
            Fail($"状态串长度不足: {data.Length}");
            return;
        }

        Status = new LoadPortStatus
        {
            IsPresent = data[0] == '1',
            IsPlaced = data[1] == '1',
            IsDeviceAlarm = data[7] == '1',
            IsDoorOpen = data[42] == 'O',
            IsDoorClosed = data[43] == 'O',
            Raw = data,
        };
        Complete();
    }
}
