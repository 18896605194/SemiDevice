using xyz.Drivers.Communication;

namespace xyz.Drivers.Rfid.FCD;

/// <summary>
/// FCD RFID 读头驱动：走 ENQ/EOT/ACK/NAK 双向握手，两个方向各一套时序。
///
/// 主机发指令：主机 ENQ → 读头 EOT → 主机发数据块 → 读头 ACK（受理）/ NAK（拒收）。
/// 读头回结果：读头 ENQ → 主机 EOT → 读头发数据块 → 主机 ACK（校验过）/ NAK（校验不过）。
///
/// 所以一条指令要走两轮握手：发出去一轮，收回来一轮。中间读头只回 ACK，结果块是它反过来发起的。
/// </summary>
public sealed class FcdRfidDriver : RfidDriverBase
{
    #region 发送相位

    /// <summary>没有在发的东西。</summary>
    private const int PhaseNone = 0;

    /// <summary>已发 ENQ，等读头回 EOT 才能把块发出去。</summary>
    private const int PhaseWaitEot = 1;

    /// <summary>块已发出，等读头 ACK/NAK。</summary>
    private const int PhaseWaitAck = 2;

    private readonly object _sendGate = new();
    private int _sendPhase = PhaseNone;
    private byte[]? _pendingBlock;

    #endregion

    public FcdRfidDriver(IFrameCommunication communication) : base(communication)
    {
    }

    /// <summary>
    /// 下发一条指令：先只发 ENQ，等读头回 EOT 再把块发出去。
    /// </summary>
    protected override void SendCommand(RfidCommand command)
    {
        lock (_sendGate)
        {
            _pendingBlock = command.BuildBlock();
            _sendPhase = PhaseWaitEot;
        }

        SendControl(FcdRfidProtocol.Enq);
    }

    public override void Close()
    {
        lock (_sendGate)
        {
            _sendPhase = PhaseNone;
            _pendingBlock = null;
        }

        base.Close();
    }

    /// <summary>
    /// 一帧到达：单字节是握手控制字符，其余按数据块处理。
    /// </summary>
    protected override void OnFrameReceived(string frame)
    {
        var bytes = FcdRfidProtocol.Binary.GetBytes(frame);
        if (bytes.Length == 0)
        {
            return;
        }

        if (bytes.Length == 1 && FcdRfidProtocol.IsControl(bytes[0]))
        {
            HandleControl(bytes[0]);
            return;
        }

        HandleBlock(bytes);
    }

    private void HandleControl(byte control)
    {
        switch (control)
        {
            case FcdRfidProtocol.Eot:
                // 读头准备好收了：把块发出去，转等 ACK。
                byte[]? block;
                lock (_sendGate)
                {
                    block = _sendPhase == PhaseWaitEot ? _pendingBlock : null;
                    if (block is not null)
                    {
                        _sendPhase = PhaseWaitAck;
                    }
                }

                if (block is not null)
                {
                    Communication.Send(FcdRfidProtocol.Binary.GetString(block));
                }

                break;

            case FcdRfidProtocol.Ack:
                // 读头受理了指令：结果块要等它反过来发起握手才到，这里只收相位。
                lock (_sendGate)
                {
                    if (_sendPhase == PhaseWaitAck)
                    {
                        _sendPhase = PhaseNone;
                        _pendingBlock = null;
                    }
                }

                break;

            case FcdRfidProtocol.Nak:
                // 读头拒收：这条指令不用再等结果了，立刻落失败。
                bool rejected;
                lock (_sendGate)
                {
                    rejected = _sendPhase == PhaseWaitAck;
                    if (rejected)
                    {
                        _sendPhase = PhaseNone;
                        _pendingBlock = null;
                    }
                }

                if (rejected)
                {
                    AbandonInflight("NAK");
                }

                break;

            case FcdRfidProtocol.Enq:
                // 读头要发东西过来（结果块或主动事件）：回 EOT 放行，块由编解码按相位收齐。
                SendControl(FcdRfidProtocol.Eot);
                break;
        }
    }

    private void HandleBlock(byte[] frame)
    {
        if (!FcdRfidProtocol.TryUnwrapBlock(frame, out byte messageId, out byte[] data))
        {
            SendControl(FcdRfidProtocol.Nak);
            return;
        }

        SendControl(FcdRfidProtocol.Ack);

        // 主动事件不认领在途指令。
        if (messageId == FcdRfidProtocol.RspEvent)
        {
            RaiseSpontaneousEvent(RfidResponse.Ok(data));
            return;
        }

        var command = Inflight;
        bool claimed = command is not null
                       && !command.IsCompleted
                       && (messageId == FcdRfidProtocol.RspError || command.ExpectedResponseIds.Contains(messageId));

        if (!claimed)
        {
            // 上电块、迟到的响应之类：当主动消息上抛，不冒充在途指令的结果。
            RaiseSpontaneousEvent(RfidResponse.Ok(data));
            return;
        }

        command!.ParseBlock(messageId, data);
        ReleaseIfCompleted();
    }

    private void SendControl(byte control)
    {
        try
        {
            Communication.Send(FcdRfidProtocol.Binary.GetString(new[] { control }));
        }
        catch
        {
            // 握手字符发不出去：连接已经出问题，由上层超时收场。
        }
    }
}
