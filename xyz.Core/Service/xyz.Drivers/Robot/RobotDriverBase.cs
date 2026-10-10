using System.Threading.Channels;
using xyz.Drivers.Communication;

namespace xyz.Drivers.Robot;

public abstract class RobotDriverBase : IRobotDriver
{
    #region 字段与构造

    protected readonly IFrameCommunication Communication;

    private readonly object _gate = new();
    private readonly Dictionary<string, RobotCommand> _inflight = new(StringComparer.OrdinalIgnoreCase);

    private Channel<string>? _rxChannel;
    private Channel<string>? _txChannel;

    public event Action<RobotDeviceEvent>? OnSpontaneousEvent;

    protected RobotDriverBase(IFrameCommunication communication)
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
    /// 返回 false 表示被拒绝：未连接、同键指令前一条未到终态或该实例已在途。
    /// 前一条的回复丢了就一直"未到终态"，要发起方等超时了用 Abandon 作废它，让出槽位。
    /// 通常由指令的 Execute 调用，不直接从外部调。
    /// </summary>
    public bool Submit(RobotCommand command)
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
                // 同键前任已终结：清掉它的在途标志，槽位让给本条。
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

        // 发送队列已经收了（正在关）：这条没发出去，占的槽位让出来。
        Abandon(command, "PortClosed");
        return false;
    }

    /// <summary>
    /// 作废一条在途指令：从在途表摘掉（同名指令能再发），指令以失败落终态。
    /// 发起方等回复超时了调；之后这条的回复要是迟到了，没有在途指令认它，当无主帧处理。任意线程可调。
    /// </summary>
    public void Abandon(RobotCommand command, string reason)
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
    /// 作废全部在途指令（关连接时用）。
    /// </summary>
    private void AbandonAll(string reason)
    {
        RobotCommand[] commands;
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
    public IReadOnlyList<RobotCommand> GetInflight()
    {
        lock (_gate)
        {
            return _inflight.Values.ToList();
        }
    }

    /// <summary>
    /// 以失败终结全部未完成的在途运动指令并腾出槽位（设备不会再回它们的结果时调用），返回被打断的条数。
    /// 只在路由消费任务上调用（如 OnCommandCompleted 内），与指令解析同线程。
    /// </summary>
    protected int InterruptMotions(string reason)
    {
        lock (_gate)
        {
            var interrupted = _inflight.Values
                .Where(command => command.IsMotion && !command.IsCompleted)
                .ToList();
            foreach (var command in interrupted)
            {
                command.Abandon(reason);
            }

            RemoveCompleted();
            return interrupted.Count;
        }
    }

    #endregion

    #region 帧路由（接收消费任务）

    private async Task RouteFramesAsync(ChannelReader<string> reader)
    {
        await foreach (string body in reader.ReadAllAsync())
        {
            RobotCommand[] commands;
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

            List<RobotCommand> finished;
            lock (_gate)
            {
                finished = RemoveCompleted();
            }

            foreach (var command in finished)
            {
                try
                {
                    OnCommandCompleted(command);
                }
                catch
                {
                    // 品牌钩子异常不拖垮路由任务。
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
    /// 移除已终结的在途指令并清在途标志；调用方须持有 _gate。
    /// </summary>
    private List<RobotCommand> RemoveCompleted()
    {
        var finished = _inflight
            .Where(pair => pair.Value.IsCompleted)
            .ToList();
        foreach (var pair in finished)
        {
            pair.Value.IsInFlight = false;
            _inflight.Remove(pair.Key);
        }

        return finished.Select(pair => pair.Value).ToList();
    }

    /// <summary>
    /// 指令到终态后的钩子（在路由消费任务上回调）；默认无，品牌驱动按需重写（如急停确认后打断在途运动）。
    /// </summary>
    protected virtual void OnCommandCompleted(RobotCommand command)
    {
    }

    /// <summary>
    /// 把无在途指令认领的帧归一化为厂商无关主动事件；默认无，品牌驱动重写。
    /// </summary>
    protected virtual RobotDeviceEvent? ParseSpontaneousEvent(string body)
    {
        return null;
    }

    /// <summary>
    /// 供路由触发主动事件；订阅方异常不影响路由任务。
    /// </summary>
    protected void RaiseSpontaneousEvent(RobotDeviceEvent evt)
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
