namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// 查询 FCD LoadPort 系统状态（GET:STATE）。64 字符状态串随 ACK 返回，ACK 即终结。
/// </summary>
public sealed class FcdGetStateCommand : FcdCommand
{
    public FcdGetStateCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    private const int StateLength = 64;

    protected override string Name => "STATE";

    /// <summary>
    /// 64 字符状态串原文。
    /// </summary>
    public string State { get; private set; } = string.Empty;

    /// <summary>
    /// 解析出的标准状态快照（E87 语义，厂商无关）；失败/长度不足时为 null。
    /// </summary>
    public LoadPortStatus? Status { get; private set; }

    protected override bool CompleteOnAck => true;

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Get}:STATE";
    }

    protected override void OnAck(string data)
    {
        if (data.Length < StateLength)
        {
            // 长度不足按失败自定终态，基类不覆盖。
            IsCompleted = true;
            IsSucceeded = false;
            Error = $"状态串长度不足: {data.Length}";
            return;
        }

        State = data;
        Status = FcdStateParser.Parse(data);
    }
}
