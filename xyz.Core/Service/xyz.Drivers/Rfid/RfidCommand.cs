namespace xyz.Drivers.Rfid;

/// <summary>
/// 一条 RFID 指令：自带下发块与认领的响应块 ID，解析到终结块时落结果。
/// 读头握手是单事务，同一时刻只有一条指令在途（由驱动保证）。
/// </summary>
public abstract class RfidCommand
{
    private volatile RfidResponse? _response;
    private volatile bool _isCompleted;

    /// <summary>指令名，诊断用。</summary>
    public abstract string Name { get; }

    /// <summary>
    /// 下发的消息块（MSG_ID + DATA），不含 LEN/校验外壳——外壳由帧编解码封。
    /// </summary>
    public abstract byte[] BuildBlock();

    /// <summary>
    /// 本指令认领的响应块 ID。错误块恒可终结任何指令，不必列在这里。
    /// </summary>
    public abstract IReadOnlyList<byte> ExpectedResponseIds { get; }

    /// <summary>
    /// 是否已终结，成功失败都算。先写结果再置位，供扫描线程读取。
    /// </summary>
    public bool IsCompleted => _isCompleted;

    /// <summary>指令结果（厂商无关），到终态前为 null。</summary>
    public RfidResponse? Response => _response;

    /// <summary>
    /// 解析一块归本指令的响应；驱动路由线程单线程调用。
    /// 解析到终结块时把厂商数据转成 RfidResponse 并调用 Complete。
    /// </summary>
    public abstract void ParseBlock(byte messageId, byte[] data);

    /// <summary>
    /// 落终态；已终结时忽略（保留首个终态）。
    /// </summary>
    protected void Complete(RfidResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (_isCompleted)
        {
            return;
        }

        _response = response;
        _isCompleted = true;
    }

    /// <summary>
    /// 由驱动在拒收/超时/掉线时落失败终态。
    /// </summary>
    internal void Abandon(string reason)
    {
        Complete(RfidResponse.Fail(reason));
    }
}
