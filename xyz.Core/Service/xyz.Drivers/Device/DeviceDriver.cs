using System.Threading.Channels;
using xyz.Common.Log;
using xyz.Drivers.Communication;

namespace xyz.Drivers.Device;

/// <summary>
/// 设备通道：下发通道（在途表、发送队列）和解析通道（解好的报文挨个交给在途指令，谁认归谁）；不认识任何品牌。
/// 收发原文都在这里记（帧体，不带壳）：状态轮询发的（指令 IsPolling）记到轮询文件，别的都进主日志
/// </summary>
public sealed class DeviceDriver
{
    #region 字段与构造

    private readonly IFrameCommunication _communication;
    private readonly IFrameLink? _link;
    private readonly ILogSource _source;
    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceCommand> _inflight = new(StringComparer.OrdinalIgnoreCase);

    // 收发队列：一轮连接一套，接收泵线程、扫描线程、重连任务都会碰，换的时候整个替换
    private volatile Channel<Received>? _rxChannel;
    private volatile Channel<string>? _txChannel;

    /// <summary>
    /// 设备主动上报：没有在途指令认的报文，在解析线程上回调，订阅方要及时返回
    /// </summary>
    public event Action<object>? Reported;

    /// <param name="communication">帧通讯（传输 + 解码器）。</param>
    /// <param name="link">有握手的协议给链路层（品牌的解码器顺带实现），没有为 null。</param>
    /// <param name="source">收发原文记在谁名下（建通道的串口 / 网口组件）。</param>
    public DeviceDriver(IFrameCommunication communication, IFrameLink? link, ILogSource source)
    {
        _communication = communication ?? throw new ArgumentNullException(nameof(communication));
        _link = link;
        _source = source;

        // 接收泵是生产者：拆好的帧和解好的报文直接入队就返回，交给指令在解析任务上做
        _communication.MessageReceived += (frame, message) => _rxChannel?.Writer.TryWrite(new Received(frame, message));
    }

    /// <summary>
    /// 收到的一帧：原文（记日志）和解好的报文（解不出来为 null）
    /// </summary>
    private sealed record Received(string Frame, object? Message);

    #endregion

    #region 连接

    /// <summary>
    /// 连接是否可用
    /// </summary>
    public bool IsConnected => _communication.IsConnected;

    /// <summary>
    /// 打开连接，起这一轮的收发任务；已经连着直接返回
    /// </summary>
    public bool Open()
    {
        try
        {
            if (IsConnected)
            {
                return true;
            }

            // 每轮连接一套内存队列和收发任务；上一轮（断线、没连上留下的）先收掉，免得旧任务一直挂着
            _rxChannel?.Writer.TryComplete();
            _txChannel?.Writer.TryComplete();
            var rx = Channel.CreateUnbounded<Received>();
            var tx = Channel.CreateUnbounded<string>();
            _rxChannel = rx;
            _txChannel = tx;
            _link?.Reset();
            _ = Task.Run(() => RouteAsync(rx.Reader));
            _ = Task.Run(() => SendAsync(tx.Reader));

            return _communication.Open();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 停止收发并关连接；在途指令全部作废——旧连接上的回复不会再来了，不作废的话同名指令再也发不出去
    /// </summary>
    public void Close()
    {
        _rxChannel?.Writer.TryComplete();
        _txChannel?.Writer.TryComplete();
        _communication.Close();
        _link?.Reset();
        AbandonAll("PortClosed");
    }

    #endregion

    #region 下发

    /// <summary>
    /// 发一条指令：占住它的在途位、放进发送队列（串行写出）；没连上、同键指令还没做完、它自己已经在途都拒（返回 false）。
    /// 前一条的回复丢了就一直没做完，要发起方等超时了用 Abandon 作废它，让出在途位
    /// </summary>
    public bool Send(DeviceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
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
                // 同键的前一条做完了：清掉它的在途标志，在途位让给这一条
                previous.IsInFlight = false;
            }

            _inflight[command.Key] = command;
            command.IsInFlight = true;
        }

        if (SendFrames(command.BuildFrame(), command.IsPolling))
        {
            return true;
        }

        // 发送队列已经收了（正在关、正在重连）：这条没发出去，占的在途位让出来
        Abandon(command, "PortClosed");
        return false;
    }

    /// <summary>
    /// 作废一条在途指令：从在途表摘掉（同键指令能再发），指令以失败落终态；之后它的回复迟到了没人认，当上报丢给 Reported。任意线程可调
    /// </summary>
    public void Abandon(DeviceCommand command, string reason)
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
    /// 作废全部在途指令（动作没做成、断线、关连接、对方拒收时用）。任意线程可调
    /// </summary>
    public void AbandonAll(string reason)
    {
        DeviceCommand[] commands;
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
    /// 一条指令的帧放进发送队列：有握手的先交链路层，它说现在发哪几帧就发哪几帧；isPolling 是状态轮询发的
    /// </summary>
    private bool SendFrames(string frame, bool isPolling)
    {
        if (_link is null)
        {
            return Enqueue(frame, isPolling);
        }

        foreach (string first in _link.Start(frame))
        {
            if (!Enqueue(first, isPolling))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 一帧放进发送队列，放进去了就记一行"发"：状态轮询的记到轮询文件，别的进主日志
    /// </summary>
    private bool Enqueue(string frame, bool isPolling)
    {
        var tx = _txChannel;
        if (tx is null || !tx.Writer.TryWrite(frame))
        {
            return false;
        }

        if (isPolling)
        {
            LogHelper.InfoLoop(_source, LogHelper.Sent, frame);
        }
        else
        {
            LogHelper.Comm(_source, LogHelper.Sent, frame);
        }

        return true;
    }

    private async Task SendAsync(ChannelReader<string> reader)
    {
        await foreach (string frame in reader.ReadAllAsync())
        {
            try
            {
                _communication.Send(frame);
            }
            catch
            {
                // 通讯故障：收掉这一轮连接退出，由上层（通道组件）重连。
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

    #region 解析

    private async Task RouteAsync(ChannelReader<Received> reader)
    {
        await foreach (var received in reader.ReadAllAsync())
        {
            try
            {
                Route(received);
            }
            catch
            {
                // 品牌代码出错不拖垮解析任务
            }
        }
    }

    /// <summary>
    /// 收到一帧：解不出来的记一行丢掉；先给链路层（握手、回执它自己处理，记进主日志），剩下的挨个给在途指令，都不认的当上报。
    /// 认领它的是状态轮询发的，原文记到轮询文件；别的指令认的、没人认的（设备主动发来的）进主日志
    /// </summary>
    private void Route(Received received)
    {
        var message = received.Message;
        if (message is null)
        {
            LogHelper.Comm(_source, LogHelper.Received, $"{received.Frame}（解不出来，丢掉）");
            return;
        }

        if (_link is not null)
        {
            var step = _link.Receive(message);
            foreach (string reply in step.Replies)
            {
                Enqueue(reply, false);
            }

            if (step.Rejected is not null)
            {
                AbandonAll(step.Rejected);
            }

            if (step.Consumed)
            {
                LogHelper.Comm(_source, LogHelper.Received, received.Frame);
                return;
            }
        }

        DeviceCommand[] commands;
        lock (_gate)
        {
            commands = _inflight.Values.ToArray();
        }

        DeviceCommand? owner = null;
        foreach (var command in commands)
        {
            if (command.Accept(message))
            {
                owner = command;
            }
        }

        if (owner is not null && owner.IsPolling)
        {
            LogHelper.InfoLoop(_source, LogHelper.Received, received.Frame);
        }
        else
        {
            LogHelper.Comm(_source, LogHelper.Received, received.Frame);
        }

        RemoveCompleted();
        if (owner is null)
        {
            RaiseReported(message);
        }
    }

    /// <summary>
    /// 做完的指令从在途表摘掉，在途位让出来
    /// </summary>
    private void RemoveCompleted()
    {
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
    }

    private void RaiseReported(object message)
    {
        try
        {
            Reported?.Invoke(message);
        }
        catch
        {
            // 订阅方异常不拖垮解析任务
        }
    }

    #endregion
}
