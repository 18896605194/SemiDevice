using System.Text;
using xyz.Drivers.Communication;

namespace xyz._35021.Module.Drivers.Fcd.Rfid;

/// <summary>
/// 富创得读头解码器：收流里只有两种东西——单字节握手控制字符，或一整块 [LEN][块][CHK][CHK]，解成 FcdRfidMessage；
/// 握手也在这里（链路层）：主机发指令 ENQ → 读头 EOT → 主机发块 → 读头 ACK / NAK；读头回结果 ENQ → 主机 EOT → 读头发块 → 主机 ACK / NAK
/// </summary>
public sealed class FcdRfidCodec : IFrameCodec, IFrameLink
{
    #region 拆帧

    /// <summary>
    /// 收块相位兜底：回 EOT 后块迟迟收不齐，当相位失效（块被截断，或读头重发了握手）。9600bps 一块百来毫秒就到，2s 余量足够；
    /// 不兜底的话半块残留会让收流永久错位
    /// </summary>
    private const int BlockPhaseTimeoutMs = 2000;

    private readonly List<byte> _rxBuffer = new();

    private bool _expectBlock;
    private int _blockPhaseTick;

    /// <summary>
    /// 二进制协议，Latin1（字节过字符串一一对应）
    /// </summary>
    public Encoding Encoding => FcdRfidProtocol.Binary;

    /// <summary>
    /// 包壳：单字节控制字符原样发（握手用），其余按数据块封 [LEN][块][CHK][CHK]
    /// </summary>
    public string Wrap(string body)
    {
        var bytes = FcdRfidProtocol.Binary.GetBytes(body);
        if (bytes.Length == 1 && FcdRfidProtocol.IsControl(bytes[0]))
        {
            return body;
        }

        return FcdRfidProtocol.Binary.GetString(FcdRfidProtocol.WrapBlock(bytes));
    }

    /// <summary>
    /// 拆帧：两种相位靠收到的字节自己定——读头发来 ENQ 之后必定跟一块，收完回到控制字节相位
    /// </summary>
    public IEnumerable<string> Extract(string chunk)
    {
        _rxBuffer.AddRange(FcdRfidProtocol.Binary.GetBytes(chunk));

        var frames = new List<string>();
        while (_rxBuffer.Count > 0)
        {
            if (!_expectBlock)
            {
                // 控制字节相位：单字节成帧。读头的 ENQ 意味着它马上要发一块过来
                byte control = _rxBuffer[0];
                _rxBuffer.RemoveAt(0);
                if (control == FcdRfidProtocol.Enq)
                {
                    _expectBlock = true;
                    _blockPhaseTick = Environment.TickCount;
                }

                frames.Add(FcdRfidProtocol.Binary.GetString(new[] { control }));
                continue;
            }

            int total = FcdRfidProtocol.FrameLength(_rxBuffer[0]);
            if (_rxBuffer.Count >= total)
            {
                frames.Add(Take(total));
                _expectBlock = false;
                continue;
            }

            if (unchecked(Environment.TickCount - _blockPhaseTick) > BlockPhaseTimeoutMs)
            {
                // 相位失效：把已收的原样交出去（校验必然不过，链路回 NAK），退回控制字节相位重新同步
                frames.Add(Take(_rxBuffer.Count));
                _expectBlock = false;
                continue;
            }

            break;
        }

        return frames;
    }

    /// <summary>
    /// 解报文：单字节控制字符解成控制帧，其余按块拆（校验不过也交出去，链路要回 NAK）
    /// </summary>
    public object? Decode(string frame)
    {
        var bytes = FcdRfidProtocol.Binary.GetBytes(frame);
        if (bytes.Length == 0)
        {
            return null;
        }

        if (bytes.Length == 1 && FcdRfidProtocol.IsControl(bytes[0]))
        {
            return new FcdRfidMessage
            {
                IsControl = true,
                Control = bytes[0],
            };
        }

        if (!FcdRfidProtocol.TryUnwrapBlock(bytes, out byte messageId, out byte[] data))
        {
            return new FcdRfidMessage
            {
                IsValid = false,
            };
        }

        return new FcdRfidMessage
        {
            IsValid = true,
            MessageId = messageId,
            Data = data,
        };
    }

    private string Take(int count)
    {
        var frame = new byte[count];
        _rxBuffer.CopyTo(0, frame, 0, count);
        _rxBuffer.RemoveRange(0, count);
        return FcdRfidProtocol.Binary.GetString(frame);
    }

    #endregion

    #region 握手（链路层）

    /// <summary>
    /// 没有在发的东西
    /// </summary>
    private const int PhaseNone = 0;

    /// <summary>
    /// 已发 ENQ，等读头回 EOT 才能把块发出去
    /// </summary>
    private const int PhaseWaitEot = 1;

    /// <summary>
    /// 块已发出，等读头 ACK / NAK
    /// </summary>
    private const int PhaseWaitAck = 2;

    private static readonly LinkStep Done = new() { Consumed = true };

    private readonly object _linkGate = new();
    private int _sendPhase = PhaseNone;
    private string? _pendingBlock;

    /// <summary>
    /// 发一条指令：先只发 ENQ，块留着等读头回 EOT 再发
    /// </summary>
    public IReadOnlyList<string> Start(string frame)
    {
        lock (_linkGate)
        {
            _pendingBlock = frame;
            _sendPhase = PhaseWaitEot;
        }

        return [ControlFrame(FcdRfidProtocol.Enq)];
    }

    /// <summary>
    /// 收到一帧：控制字符都是握手自己的事；块校验过了回 ACK 再交给指令（主动事件、迟到的没指令认，当上报），校验不过回 NAK
    /// </summary>
    public LinkStep Receive(object message)
    {
        if (message is FcdRfidMessage rfid)
        {
            if (rfid.IsControl)
            {
                return ReceiveControl(rfid.Control);
            }

            if (!rfid.IsValid)
            {
                return new LinkStep { Replies = [ControlFrame(FcdRfidProtocol.Nak)], Consumed = true };
            }

            return new LinkStep { Replies = [ControlFrame(FcdRfidProtocol.Ack)] };
        }

        return LinkStep.PassOn;
    }

    /// <summary>
    /// 连接开了或关了：发到一半的作废
    /// </summary>
    public void Reset()
    {
        lock (_linkGate)
        {
            _sendPhase = PhaseNone;
            _pendingBlock = null;
        }
    }

    private LinkStep ReceiveControl(byte control)
    {
        switch (control)
        {
            case FcdRfidProtocol.Eot:
                return SendPendingBlock();

            case FcdRfidProtocol.Ack:
                // 读头受理了指令：结果块要等它反过来发起握手才到，这里只收相位
                lock (_linkGate)
                {
                    if (_sendPhase == PhaseWaitAck)
                    {
                        _sendPhase = PhaseNone;
                        _pendingBlock = null;
                    }
                }

                return Done;

            case FcdRfidProtocol.Nak:
                return RejectPending();

            case FcdRfidProtocol.Enq:
                // 读头要发东西过来（结果块或主动事件）：回 EOT 放行，块由拆帧按相位收齐
                return new LinkStep { Replies = [ControlFrame(FcdRfidProtocol.Eot)], Consumed = true };

            default:
                return Done;
        }
    }

    /// <summary>
    /// 读头准备好收了：把留着的块发出去，转等 ACK
    /// </summary>
    private LinkStep SendPendingBlock()
    {
        string? block = null;
        lock (_linkGate)
        {
            if (_sendPhase == PhaseWaitEot && _pendingBlock is not null)
            {
                block = _pendingBlock;
                _sendPhase = PhaseWaitAck;
            }
        }

        if (block is null)
        {
            return Done;
        }

        return new LinkStep { Replies = [block], Consumed = true };
    }

    /// <summary>
    /// 读头拒收：这条指令不用再等结果了，立刻落失败（不白等超时）
    /// </summary>
    private LinkStep RejectPending()
    {
        bool rejected;
        lock (_linkGate)
        {
            rejected = _sendPhase == PhaseWaitAck;
            if (rejected)
            {
                _sendPhase = PhaseNone;
                _pendingBlock = null;
            }
        }

        if (!rejected)
        {
            return Done;
        }

        return new LinkStep { Consumed = true, Rejected = "NAK" };
    }

    private static string ControlFrame(byte control)
    {
        return FcdRfidProtocol.Binary.GetString(new[] { control });
    }

    #endregion
}
