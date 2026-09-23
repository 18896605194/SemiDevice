using xyz.Drivers.Communication;

namespace xyz.Drivers.Rfid.FCD;

/// <summary>
/// FCD RFID 帧编解码：收流里只有两种东西——单字节握手控制字符，或一整块 [LEN][块][CHK][CHK]。
/// 两者靠"收块相位"区分：读头发来 ENQ 之后必定紧跟一块，收完即回到控制字节相位。
/// 相位完全由收到的字节决定，不需要驱动告知。自持接收缓冲，每通道一个实例。
/// </summary>
public sealed class FcdRfidFrameCodec : IFrameCodec
{
    /// <summary>
    /// 收块相位 T1 兜底：回 EOT 后块迟迟收不齐，视为相位失效（块被截断，或读头重发了握手）。
    /// 正常一块 9600bps 下百来毫秒就到，2s 余量足够；不兜底的话半块残留会让收流永久错位。
    /// </summary>
    private const int BlockPhaseTimeoutMs = 2000;

    private readonly List<byte> _rxBuffer = new();

    private bool _expectBlock;
    private int _blockPhaseTick;

    /// <summary>
    /// 发送封壳：单字节控制字符原样发（握手用），其余按数据块封 [LEN][块][CHK][CHK]。
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

    public IEnumerable<string> Extract(string chunk)
    {
        _rxBuffer.AddRange(FcdRfidProtocol.Binary.GetBytes(chunk));

        var frames = new List<string>();
        while (_rxBuffer.Count > 0)
        {
            if (!_expectBlock)
            {
                // 控制字节相位：单字节成帧。读头的 ENQ 意味着它马上要发一块过来。
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
                // 相位失效：把已收的原样抛给驱动（校验必然不过，驱动回 NAK），并退回控制字节相位重新同步。
                frames.Add(Take(_rxBuffer.Count));
                _expectBlock = false;
                continue;
            }

            break;
        }

        return frames;
    }

    private string Take(int count)
    {
        var frame = new byte[count];
        _rxBuffer.CopyTo(0, frame, 0, count);
        _rxBuffer.RemoveRange(0, count);
        return FcdRfidProtocol.Binary.GetString(frame);
    }
}
