using System.Threading.Channels;
using xyz.Drivers.Communication;

namespace xyz.Drivers.Loadport;

/// <summary>
/// LoadPort 驱动基类：承载品牌无关的指令受理与帧路由机制
/// （在途槽位、收发内存队列、无主帧主动事件），具体品牌只补设备语义。
/// 动作返回 true 表示设备已确认动作完成，而非仅发送成功。
/// </summary>
public abstract class LoadPortDriverBase : ILoadPortDriver
{
    #region 字段与构造

    /// <summary>
    /// 帧通讯（传输 + 帧编解码），构造注入。
    /// </summary>
    protected readonly IFrameCommunication Communication;

    private readonly object _gate = new();
    private readonly Dictionary<string, LoadPortCommand> _inflight = new(StringComparer.OrdinalIgnoreCase);

    // 收发队列：一轮连接一套，接收泵线程、扫描线程、重连任务都会碰，换的时候整个替换。
    private volatile Channel<string>? _rxChannel;
    private volatile Channel<string>? _txChannel;

    /// <summary>
    /// 设备主动上报事件：无在途指令认领的帧经 ParseSpontaneousEvent 归一化后触发，
    /// 在路由消费任务上回调，订阅方应及时返回。
    /// </summary>
    public event Action<LoadPortDeviceEvent>? OnSpontaneousEvent;

    protected LoadPortDriverBase(IFrameCommunication communication)
    {
        Communication = communication ?? throw new ArgumentNullException(nameof(communication));

        // 帧通讯接收泵是生产者：收到完整帧立即入队返回，不做任何解析。
        Communication.FrameReceived += body => _rxChannel?.Writer.TryWrite(body);
    }

    #endregion

    #region 连接

    /// <summary>
    /// 设备连接是否可用。
    /// </summary>
    public virtual bool IsConnected => Communication.IsConnected;

    /// <summary>
    /// 打开帧通讯并启动收发消费任务。
    /// </summary>
    public virtual bool Open()
    {
        try
        {
            if (IsConnected)
            {
                return true;
            }

            // 每个连接周期一套内存队列与消费任务；上一轮的（断线、没连上留下的）先收掉，免得旧任务一直挂着。
            _rxChannel?.Writer.TryComplete();
            _txChannel?.Writer.TryComplete();
            var rx = Channel.CreateUnbounded<string>();
            var tx = Channel.CreateUnbounded<string>();
            _rxChannel = rx;
            _txChannel = tx;
            _ = Task.Run(() => RouteFramesAsync(rx.Reader));
            _ = Task.Run(() => SendFramesAsync(tx.Reader));

            return Communication.Open();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 停止收发并关闭帧通讯；在途指令全部作废——旧连接上的回复不会再来了，不作废的话同名指令再也发不出去。
    /// </summary>
    public virtual void Close()
    {
        _rxChannel?.Writer.TryComplete();
        _txChannel?.Writer.TryComplete();
        Communication.Close();
        AbandonAll("PortClosed");
    }

    #endregion

    #region 指令受理

    /// <summary>
    /// 受理一条指令：占在途槽位并下发（经发送队列串行写出）。
    /// 返回 false 表示被拒绝：未连接、同名指令前一条未到终态或该实例已在途。
    /// 前一条的回复丢了就一直"未到终态"，要发起方等超时了用 Abandon 作废它，让出槽位。
    /// 通常由指令的 Execute 调用，不直接从外部调。
    /// </summary>
    public bool Submit(LoadPortCommand command)
    {
        lock (_gate)
        {
            if (!IsConnected || command.IsInFlight)
            {
                return false;
            }

            if (_inflight.TryGetValue(command.Key, out var previous) && !previous.IsCompleted)
            {
                return false;
            }

            if (previous is not null)
            {
                // 同名前任已终结：清掉它的在途标志，槽位让给本条。
                previous.IsInFlight = false;
            }

            _inflight[command.Key] = command;
            command.IsInFlight = true;
        }

        var tx = _txChannel;
        if (tx is not null && tx.Writer.TryWrite(command.BuildMsg()))
        {
            return true;
        }

        // 发送队列已经收了（正在关、正在重连）：这条没发出去，占的槽位让出来。
        Abandon(command, "PortClosed");
        return false;
    }

    /// <summary>
    /// 作废一条在途指令：从在途表摘掉（同名指令能再发），指令以失败落终态。
    /// 发起方等回复超时了调；之后这条的回复要是迟到了，没有在途指令认它，当无主帧丢掉。任意线程可调。
    /// </summary>
    public void Abandon(LoadPortCommand command, string reason)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_gate)
        {
            if (_inflight.TryGetValue(command.Key, out var current) && ReferenceEquals(current, command))
            {
                _inflight.Remove(command.Key);
            }

            command.IsInFlight = false;
        }

        command.Abandon(reason);
    }

    /// <summary>
    /// 作废全部在途指令（动作没做成、断线、关连接时用）。任意线程可调。
    /// </summary>
    public void AbandonAll(string reason)
    {
        LoadPortCommand[] commands;
        lock (_gate)
        {
            commands = _inflight.Values.ToArray();
            _inflight.Clear();
            foreach (var command in commands)
            {
                command.IsInFlight = false;
            }
        }

        foreach (var command in commands)
        {
            command.Abandon(reason);
        }
    }

    /// <summary>
    /// 当前在途指令快照，供诊断/轮询；发起方通常直接轮询自己持有的指令实例。
    /// </summary>
    public IReadOnlyList<LoadPortCommand> GetInflight()
    {
        lock (_gate)
        {
            return _inflight.Values.ToList();
        }
    }

    #endregion

    #region 帧路由（接收消费任务）

    private async Task RouteFramesAsync(ChannelReader<string> reader)
    {
        await foreach (string body in reader.ReadAllAsync())
        {
            LoadPortCommand[] commands;
            lock (_gate)
            {
                commands = _inflight.Values.ToArray();
            }

            bool claimed = false;
            foreach (var command in commands)
            {
                if (command.ParseMsg(body))
                {
                    claimed = true;
                }
            }

            lock (_gate)
            {
                var finished = _inflight
                    .Where(pair => pair.Value.IsCompleted)
                    .ToList();
                foreach (var pair in finished)
                {
                    pair.Value.IsInFlight = false;
                    _inflight.Remove(pair.Key);
                }
            }

            if (!claimed)
            {
                var evt = ParseSpontaneousEvent(body);
                if (evt is not null)
                {
                    RaiseSpontaneousEvent(evt);
                }
            }
        }
    }

    /// <summary>
    /// 把无在途指令认领的帧归一化为厂商无关主动事件；默认无，品牌驱动重写。
    /// </summary>
    protected virtual LoadPortDeviceEvent? ParseSpontaneousEvent(string body)
    {
        return null;
    }

    /// <summary>
    /// 供路由触发主动事件；订阅方异常不影响路由任务。
    /// </summary>
    protected void RaiseSpontaneousEvent(LoadPortDeviceEvent evt)
    {
        try
        {
            OnSpontaneousEvent?.Invoke(evt);
        }
        catch
        {
            // 订阅方异常不拖垮路由任务。
        }
    }

    #endregion

    #region 发送（消费任务）

    private async Task SendFramesAsync(ChannelReader<string> reader)
    {
        await foreach (string body in reader.ReadAllAsync())
        {
            try
            {
                Communication.Send(body);
            }
            catch
            {
                // 通讯故障：收掉这一轮连接退出，由上层（驱动组件）重连。
                // 只收自己这一轮：重连以后才醒过来的旧发送任务不能把新连接关了。
                if (ReferenceEquals(_txChannel?.Reader, reader))
                {
                    Close();
                }

                break;
            }
        }
    }

    #endregion
}
