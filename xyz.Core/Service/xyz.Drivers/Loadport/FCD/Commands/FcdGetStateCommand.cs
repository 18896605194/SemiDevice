namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// 查询 FCD LoadPort 系统状态（GET:STATE）。64 字符状态串随 ACK 返回，ACK 即终结；
/// 状态串解析成 Response.Status（E87 语义，厂商无关）。
/// </summary>
public sealed class FcdGetStateCommand : FcdCommand
{
    public FcdGetStateCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    private const int StateLength = 64;

    protected override string Name => "STATE";

    protected override bool CompleteOnAck => true;

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Get}:STATE";
    }

    protected override LoadPortResponse BuildResponse(string data)
    {
        if (data.Length < StateLength)
        {
            return new LoadPortResponse
            {
                IsSuccess = false,
                Error = $"状态串长度不足: {data.Length}",
                Content = data,
            };
        }

        // 协议状态串：第 1/2/8 位为在位、放好、报警，第 43/44 位为门开、门关。
        // 其余位待协议手册确认。
        return new LoadPortResponse
        {
            IsSuccess = true,
            Content = data,
            Status = new LoadPortStatus
            {
                PodPresent = data[0] == '1',
                PodPlaced = data[1] == '1',
                DeviceAlarm = data[7] == '1',
                DoorOpen = data[42] == 'O',
                DoorClosed = data[43] == 'O',
                Raw = data,
            },
        };
    }
}
