using xyz.Drivers.Communication;

namespace xyz.Drivers.Rfid;

/// <summary>
/// RFID 驱动基类：承载品牌无关的连接与在途受理（读头握手是单事务，同一时刻只收一条指令），
/// 具体的握手时序与块语义由品牌驱动补。
/// 与 LoadPort 驱动不同的是这里没有按指令名分槽：读头一次只认一条。
/// </summary>
public abstract class RfidDriverBase : IRfidDriver
{
    #region 字段与构造

    /// <summary>帧通讯（传输 + 帧编解码），构造注入。</summary>
    protected readonly IFrameCommunication Communication;

    private readonly object _gate = new();
    private RfidCommand? _inflight;

    /// <summary>
    /// 读头主动上报（如 0x66 事件块）：无在途指令认领的块在路由线程回调，订阅方应及时返回。
    /// </summary>
    public event Action<RfidResponse>? OnSpontaneousEvent;

    protected RfidDriverBase(IFrameCommunication communication)
    {
        Communication = communication ?? throw new ArgumentNullException(nameof(communication));
        Communication.FrameReceived += OnFrameReceived;
    }

    #endregion

    #region 连接

    public virtual bool IsConnected => Communication.IsConnected;

    public virtual bool Open()
    {
        try
        {
            return IsConnected || Communication.Open();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 关闭连接；在途指令以掉线落失败，避免上层一直等一个永远不来的回复。
    /// </summary>
    public virtual void Close()
    {
        AbandonInflight("PortClosed");
        Communication.Close();
    }

    #endregion

    #region 指令受理

    /// <summary>
    /// 当前在途指令；无在途为 null。
    /// </summary>
    protected RfidCommand? Inflight
    {
        get
        {
            lock (_gate)
            {
                return _inflight;
            }
        }
    }

    /// <summary>
    /// 受理一条指令：占住唯一的在途位并下发。
    /// 返回 false 表示被拒绝——未连接，或上一条还没终结。
    /// </summary>
    public bool Submit(RfidCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            if (!IsConnected)
            {
                return false;
            }

            if (_inflight is not null && !_inflight.IsCompleted)
            {
                return false;
            }

            _inflight = command;
        }

        try
        {
            SendCommand(command);
            return true;
        }
        catch
        {
            AbandonInflight("SendFailed");
            return false;
        }
    }

    /// <summary>
    /// 把在途指令以失败终结并让出在途位（拒收、超时、掉线时用）。
    /// </summary>
    public void AbandonInflight(string reason)
    {
        RfidCommand? command;
        lock (_gate)
        {
            command = _inflight;
            _inflight = null;
        }

        command?.Abandon(reason);
    }

    /// <summary>
    /// 在途指令已终结就让出在途位。
    /// </summary>
    protected void ReleaseIfCompleted()
    {
        lock (_gate)
        {
            if (_inflight is not null && _inflight.IsCompleted)
            {
                _inflight = null;
            }
        }
    }

    /// <summary>
    /// 下发一条指令（品牌握手时序由此展开）；由 Submit 在占位成功后调用。
    /// </summary>
    protected abstract void SendCommand(RfidCommand command);

    /// <summary>
    /// 收到一帧（已由帧编解码拆好：单字节控制帧或一整块）；品牌驱动按自己的时序处理。
    /// </summary>
    protected abstract void OnFrameReceived(string frame);

    #endregion

    /// <summary>
    /// 供品牌驱动上抛主动事件；订阅方异常不影响路由。
    /// </summary>
    protected void RaiseSpontaneousEvent(RfidResponse response)
    {
        try
        {
            OnSpontaneousEvent?.Invoke(response);
        }
        catch
        {
            // 订阅方异常不拖垮接收泵。
        }
    }
}
