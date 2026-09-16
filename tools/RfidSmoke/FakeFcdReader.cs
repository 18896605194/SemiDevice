using System.Collections.Concurrent;
using xyz.Drivers.Communication;
using xyz.Drivers.Rfid.FCD;

namespace RfidSmoke;

/// <summary>
/// 假读头：顶替串口，按 FCD RFT-200S 的真实时序跟驱动对话。
///
/// 收指令：主机 ENQ → 本机 EOT → 主机发块 → 本机 ACK/NAK
/// 回结果：本机 ENQ → 主机 EOT → 本机发块 → 主机 ACK/NAK
///
/// 故障注入靠几个开关：不回结果（超时）、回错误块、拒收、发坏校验块。
/// </summary>
internal sealed class FakeFcdReader : ICommunication
{
    private readonly BlockingCollection<byte[]> _toHost = new();

    /// <summary>主机发过来的完整块，供断言核对。</summary>
    public readonly ConcurrentQueue<byte[]> HostBlocks = new();

    /// <summary>标签内存内容（0xE0 响应里页头之后的部分）。</summary>
    public byte[] TagMemory { get; set; } = System.Text.Encoding.ASCII.GetBytes("FOUP-0001       ");

    /// <summary>收到读码指令后不回结果：用来验超时。</summary>
    public bool SwallowResult { get; set; }

    /// <summary>收到块后回 NAK 而不是 ACK：用来验拒收。</summary>
    public bool RejectCommand { get; set; }

    /// <summary>回错误块 0x63 而不是数据块。</summary>
    public byte? ErrorCode { get; set; }

    /// <summary>回结果块时故意写坏校验和：用来验主机 NAK。</summary>
    public bool CorruptChecksum { get; set; }

    /// <summary>主机对坏块回过几次 NAK。</summary>
    public int HostNakCount;

    public bool IsConnected { get; private set; }

    public void Connect()
    {
        IsConnected = true;
    }

    public void Close()
    {
        IsConnected = false;
    }

    public void Dispose()
    {
        _toHost.Dispose();
    }

    /// <summary>
    /// 主机侧的接收泵从这里拿字节；空闲时抛 TimeoutException（FrameCommunication 会当正常空闲继续轮询）。
    /// </summary>
    public byte[] Receive()
    {
        if (_toHost.TryTake(out var data, 20))
        {
            return data;
        }

        throw new TimeoutException();
    }

    #region 读头侧时序

    /// <summary>主机发来的下一段是不是数据块（本机回过 EOT 之后）。</summary>
    private bool _expectBlockFromHost;

    /// <summary>本机已发出结果块，正等主机 ACK/NAK。</summary>
    private bool _waitingHostAck;

    private byte[]? _pendingResult;

    public void Send(byte[] data)
    {
        foreach (byte value in data)
        {
            Feed(value);
        }
    }

    private readonly List<byte> _fromHost = new();

    private void Feed(byte value)
    {
        if (!_expectBlockFromHost)
        {
            HandleHostControl(value);
            return;
        }

        _fromHost.Add(value);
        if (_fromHost.Count < FcdRfidProtocol.FrameLength(_fromHost[0]))
        {
            return;
        }

        var frame = _fromHost.ToArray();
        _fromHost.Clear();
        _expectBlockFromHost = false;
        HandleHostBlock(frame);
    }

    private void HandleHostControl(byte control)
    {
        switch (control)
        {
            case FcdRfidProtocol.Enq:
                // 主机要发指令：放行。
                _expectBlockFromHost = true;
                Post(FcdRfidProtocol.Eot);
                break;

            case FcdRfidProtocol.Eot:
                // 主机准备好收本机的结果块了。
                if (_pendingResult is not null)
                {
                    Post(_pendingResult);
                    _pendingResult = null;
                    _waitingHostAck = true;
                }

                break;

            case FcdRfidProtocol.Ack:
                _waitingHostAck = false;
                break;

            case FcdRfidProtocol.Nak:
                if (_waitingHostAck)
                {
                    Interlocked.Increment(ref HostNakCount);
                    _waitingHostAck = false;
                }

                break;
        }
    }

    private void HandleHostBlock(byte[] frame)
    {
        HostBlocks.Enqueue(frame);

        if (RejectCommand)
        {
            Post(FcdRfidProtocol.Nak);
            return;
        }

        Post(FcdRfidProtocol.Ack);

        if (SwallowResult)
        {
            return;
        }

        if (!FcdRfidProtocol.TryUnwrapBlock(frame, out byte messageId, out _))
        {
            return;
        }

        byte[]? block = messageId switch
        {
            FcdRfidProtocol.CmdReadTag => BuildTagBlock(),
            FcdRfidProtocol.CmdGetVersion => [FcdRfidProtocol.RspVersion, 0x02, 0x04],
            FcdRfidProtocol.CmdGetStatus => [FcdRfidProtocol.RspStatus, 0x00],
            _ => null,
        };

        if (block is null)
        {
            return;
        }

        if (ErrorCode is { } code)
        {
            block = [FcdRfidProtocol.RspError, code];
        }

        var wrapped = FcdRfidProtocol.WrapBlock(block);
        if (CorruptChecksum)
        {
            wrapped[^1] ^= 0xFF;
        }

        // 反向握手：本机先 ENQ，等主机 EOT 再发块。
        _pendingResult = wrapped;
        Post(FcdRfidProtocol.Enq);
    }

    /// <summary>0xE0 数据块：[0xE0][起始页][页数][标签内存...]。</summary>
    private byte[] BuildTagBlock()
    {
        var block = new byte[3 + TagMemory.Length];
        block[0] = FcdRfidProtocol.RspTagData;
        block[1] = 0x00;                                  // 起始页
        block[2] = (byte)Math.Max(1, TagMemory.Length / 8); // 页数
        Array.Copy(TagMemory, 0, block, 3, TagMemory.Length);
        return block;
    }

    /// <summary>读头主动事件块 0x66，用来验它不会被当成在途指令的结果。</summary>
    public void PushSpontaneousEvent(byte payload)
    {
        _pendingResult = FcdRfidProtocol.WrapBlock([FcdRfidProtocol.RspEvent, payload]);
        Post(FcdRfidProtocol.Enq);
    }

    private void Post(byte value)
    {
        Post([value]);
    }

    private void Post(byte[] data)
    {
        if (!_toHost.IsAddingCompleted)
        {
            _toHost.Add(data);
        }
    }

    #endregion
}
