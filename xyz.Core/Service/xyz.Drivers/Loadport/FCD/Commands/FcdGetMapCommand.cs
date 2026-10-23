namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// 取槽图（GET:MAPDT）：设备回最后一次扫片的结果（正序，第 1 个字符是第 1 槽），槽位串随 ACK 返回、ACK 即终结；
/// 归一化进 Response.SlotMap。
/// </summary>
public sealed class FcdGetMapCommand : FcdCommand
{
    public FcdGetMapCommand(ILoadPortDriver driver) : base(driver)
    {
    }

    protected override string Name => "MAPDT";

    protected override bool CompleteOnAck => true;

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Get}:MAPDT";
    }

    protected override LoadPortResponse BuildResponse(string data)
    {
        return new LoadPortResponse
        {
            IsSuccess = true,
            Content = data,
            SlotMap = FcdProtocol.ParseSlotMap(data),
        };
    }
}
