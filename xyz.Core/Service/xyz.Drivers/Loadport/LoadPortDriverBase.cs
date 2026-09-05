using System.Threading.Channels;
using xyz.Drivers.Communication;

namespace xyz.Drivers.Loadport;

/// <summary>
/// LoadPort 驱动基类：承载品牌无关的指令受理与帧路由机制
/// （在途槽位、收发内存队列、无主帧主动事件），具体品牌只补设备语义。
/// 动作返回 true 表示设备已确认动作完成，而非仅发送成功。
/// </summary>
public abstract class LoadPortDriverBase
{
    #region 字段与构造

    /// <summary>
    /// 帧通讯（传输 + 帧编解码），构造注入。
    /// </summary>
    protected readonly IFrameCommunication Communication;

    private readonly object _gate = new();
    private readonly Dictionary<string, LoadPortCommand> _inflight = new(StringComparer.OrdinalIgnoreCase);

    private Channel<string>? _rxChannel;
    private Channel<string>? _txChannel;

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

            // 每个连接周期一套内存队列与消费任务；Close 后不可复用。
            _rxChannel = Channel.CreateUnbounded<string>();
            _txChannel = Channel.CreateUnbounded<string>();
            _ = Task.Run(() => RouteFramesAsync(_rxChannel.Reader));
            _ = Task.Run(() => SendFramesAsync(_txChannel.Reader));

            return Communication.Open();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 停止收发并关闭帧通讯。
    /// </summary>
    public virtual void Close()
    {
        _rxChannel?.Writer.TryComplete();
        _txChannel?.Writer.TryComplete();
        Communication.Close();
    }

    #endregion

    #region 指令受理

    /// <summary>
    /// 受理一条指令：占在途槽位并下发（经发送队列串行写出）。
    /// 返回 false 表示被拒绝：未连接、同名指令前一条未到终态或该实例已在途。
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
        return tx is not null && tx.Writer.TryWrite(command.BuildMsg());
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
                // 通讯故障：收尾退出，由上层决定恢复策略。
                Close();
                break;
            }
        }
    }

    #endregion
}
