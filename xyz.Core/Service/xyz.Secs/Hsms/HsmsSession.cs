using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using xyz.Secs.Diagnostics;
using xyz.Secs.SecsII;

namespace xyz.Secs.Hsms;

/// <summary>
/// 一条 HSMS-SS 连接的协议机：接收泵、发送串行化、Select 交接、事务配对（SystemBytes）、
/// T3/T6/T7/T8 定时器和 Linktest 心跳都收在这里。Connector（主动连出）和 Listener（被动监听）
/// 负责建 TCP，建好之后都交给本类。
/// 线程模型：泵与控制循环（Select/Linktest/T7）是同步阻塞循环，由 Task.Factory.StartNew +
/// LongRunning 跑在 TPL 专用线程上（和 FrameCommunication.PumpLoop 同款）——LongRunning 只有配
/// 同步委托才真正生效，配 async 委托专用线程只活到第一个 await。
/// IO 走同步 Read/Write：T8 用 NetworkStream.ReadTimeout 实现（开帧后每一跳受限，帧间空闲不限、靠 Linktest 探活）。
/// 对外的 SendAsync 只是等回复的薄壳（TaskCompletionSource + WaitAsync），挂在调用方上下文，不占泵线程。
/// </summary>
public sealed class HsmsSession : IDisposable
{
    private const string Category = "Hsms";

    private readonly Stream _stream;
    private readonly HsmsSettings _settings;
    private readonly ISecsSink _sink;
    private readonly bool _initiateSelect;
    private readonly object _sendGate = new();

    /// <summary>数据事务表：我发出去的 primary 的 SystemBytes → 等 secondary。</summary>
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<HsmsMessage>> _transactions = new();

    /// <summary>控制事务表：Select/Deselect/Linktest 的 SystemBytes → 等 rsp。</summary>
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<HsmsHeader>> _controls = new();

    private int _systemBytes = Random.Shared.Next(1, int.MaxValue);  // 重连后换起始号，降低与对端撞号概率
    private volatile HsmsLinkState _state;
    private int _closed;
    private int _started;
    private int _heartbeatStarted;
    private bool _readTimeoutSupported = true;

    /// <summary>对方主动发来的数据消息（S1F13、S2F41 这类）。在泵线程触发，订阅方要快进快出，
    /// 尤其不能同步等 SendAsync（回复要靠泵线程派发，会把泵堵死）。</summary>
    public event Action<HsmsMessage>? PrimaryReceived;

    /// <summary>Select 交接成功，链路进入 SELECTED。</summary>
    public event Action? Selected;

    /// <summary>连接断开（带原因）。在泵线程或控制线程触发。</summary>
    public event Action<string>? Closed;

    public HsmsLinkState State => _state;

    public bool IsSelected => _state == HsmsLinkState.Selected;

    internal HsmsSession(Stream stream, HsmsSettings settings, ISecsSink? sink = null, bool initiateSelect = false)
    {
        _stream = stream;
        _settings = settings;
        _sink = sink ?? NullSecsSink.Instance;
        _initiateSelect = initiateSelect;
    }

    /// <summary>
    /// 启动协议机：开接收泵；主动方自动发起 Select（T6），被动方起 T7 计时等对端 Select。
    /// 三个循环都是同步阻塞体，各占一条 LongRunning 专用线程。
    /// </summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _state = HsmsLinkState.ConnectedNotSelected;
        _ = Task.Factory.StartNew(PumpLoop, TaskCreationOptions.LongRunning);
        if (_initiateSelect)
        {
            _ = Task.Factory.StartNew(SelectLoop, TaskCreationOptions.LongRunning);
        }
        else
        {
            _ = Task.Factory.StartNew(WatchNotSelected, TaskCreationOptions.LongRunning);
        }
    }

    #region 发送

    /// <summary>
    /// 发 primary 并等 secondary：W=1 专用，T3 到期抛 SecsTimeoutException。
    /// 等待挂在调用方上下文（WaitAsync 不占线程），可以放心 await；但别在 PrimaryReceived 回调里同步 Wait 它。
    /// </summary>
    public async Task<HsmsMessage> SendAsync(SecsMessage message, CancellationToken cancellationToken = default)
    {
        if (!message.ReplyExpected)
        {
            throw new SecsException($"{message.Name} W=0 没有回复，用 Send 发送");
        }

        var tcs = SendCore(message, out var systemBytes);
        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(_settings.T3ReplyTimeoutMs), cancellationToken);
        }
        catch (TimeoutException)
        {
            _transactions.TryRemove(systemBytes, out _);
            throw new SecsTimeoutException($"T3 等回复超时: {message.Name} Sys={systemBytes}");
        }
    }

    /// <summary>
    /// 发 primary 不等回复：W=0 用这个；W=1 也允许（事务仍挂表，回复没人领 T3 到期自动清理，不会误配后续报文）。
    /// </summary>
    public void Send(SecsMessage message)
    {
        SendCore(message, out _);
    }

    /// <summary>
    /// 回对方的 primary：SystemBytes 取自 primary，secondary 不带 W-Bit。
    /// 一般配合 primary.CreateReply(body) 使用，天然带回同号。
    /// </summary>
    public void Reply(HsmsMessage primary, SecsMessage reply)
    {
        var header = HsmsHeader.CreateData(_settings.DeviceId, reply.Stream, reply.Function,
            replyExpected: false, primary.Header.SystemBytes);
        WriteFrame(header, reply.Body);
    }

    private TaskCompletionSource<HsmsMessage> SendCore(SecsMessage message, out uint systemBytes)
    {
        if (!IsSelected)
        {
            throw new HsmsConnectionException($"未 SELECTED，不能发 {message.Name}");
        }

        systemBytes = message.SystemBytes != 0 ? message.SystemBytes : NextSystemBytes();
        message.SystemBytes = systemBytes;
        var header = HsmsHeader.CreateData(_settings.DeviceId, message.Stream, message.Function, message.ReplyExpected, systemBytes);
        var tcs = new TaskCompletionSource<HsmsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (message.ReplyExpected)
        {
            _transactions[systemBytes] = tcs;
        }

        WriteFrame(header, message.Body);
        return tcs;
    }

    /// <summary>发 Select/Linktest/Deselect 这类控制事务并等 rsp（同步，跑在控制线程上），T6 到期抛 SecsTimeoutException。</summary>
    private HsmsHeader SendControl(HsmsMessageType type, int timeoutMs)
    {
        if (_closed == 1)
        {
            throw new HsmsConnectionException("连接已关闭");
        }

        uint systemBytes = NextSystemBytes();
        var tcs = new TaskCompletionSource<HsmsHeader>(TaskCreationOptions.RunContinuationsAsynchronously);
        _controls[systemBytes] = tcs;
        WriteFrame(HsmsHeader.CreateControl(type, systemBytes), null);
        try
        {
            if (!tcs.Task.Wait(timeoutMs))
            {
                throw new SecsTimeoutException($"T6 控制事务超时: {type}");
            }

            return tcs.Task.Result;
        }
        catch (AggregateException exception)
        {
            // 对端拒绝/断线时事务被打失败，解包保留原始异常
            ExceptionDispatchInfo.Capture(exception.InnerException!).Throw();
            throw;  // 不可达
        }
        finally
        {
            _controls.TryRemove(systemBytes, out _);
        }
    }

    /// <summary>优雅断开：发 Separate.req 后关连接。单向消息，不等回复。</summary>
    public void SendSeparate()
    {
        try
        {
            WriteFrame(HsmsHeader.CreateControl(HsmsMessageType.SeparateReq, NextSystemBytes()), null);
        }
        catch (SecsException)
        {
            // 线都要断了，发不出去就算了
        }
        Close("本端 Separate");
    }

    /// <summary>
    /// 写一帧：4 字节大端长度 + 10 字节头 +（数据消息）Item 树。
    /// 工控报文都是 KB 级小帧，同步写最简单；锁保证控制消息与数据消息不交错。
    /// </summary>
    private void WriteFrame(HsmsHeader header, SecsItem? body)
    {
        if (_closed == 1)
        {
            throw new HsmsConnectionException("连接已关闭");
        }

        byte[] item = body is null ? [] : SecsCodec.Encode(body);
        var frame = new byte[4 + HsmsHeader.Size + item.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, HsmsHeader.Size + item.Length);
        header.Write(frame.AsSpan(4, HsmsHeader.Size));
        item.CopyTo(frame, 4 + HsmsHeader.Size);

        lock (_sendGate)
        {
            if (_closed == 1)
            {
                throw new HsmsConnectionException("连接已关闭");
            }

            try
            {
                _stream.Write(frame, 0, frame.Length);
                _stream.Flush();
            }
            catch (Exception exception)
            {
                Close($"发送失败: {exception.Message}");
                throw new HsmsConnectionException($"发送 {header.MessageType} 失败: {exception.Message}");
            }
        }

        _sink.Trace(SecsMessageDirection.Sent, new HsmsMessage(header, body));
    }

    #endregion

    #region 接收泵

    /// <summary>接收泵：同步阻塞循环，LongRunning 专用线程。断线/超时/协议错误都走 Close 收尾。</summary>
    private void PumpLoop()
    {
        try
        {
            while (true)
            {
                var frame = ReadFrame();
                if (frame is null)
                {
                    Close("对端关闭连接");
                    return;
                }

                Dispatch(frame);
            }
        }
        catch (SecsTimeoutException exception)
        {
            Close(exception.Message);
        }
        catch (Exception exception)
        {
            Close($"接收异常: {exception.Message}");
        }
    }

    /// <summary>
    /// 读一帧：先读 4 字节长度前缀再读整帧。
    /// T8 语义靠 ReadTimeout：等首字节不限时（帧间空闲靠 Linktest 探活），
    /// 首字节一到、帧没读完的每一跳都受 T8 约束（NetworkStream 的 ReadTimeout 每次读都生效）。
    /// 返回 null 表示对端关闭。
    /// </summary>
    private byte[]? ReadFrame()
    {
        var prefix = new byte[4];
        if (!ReadExact(prefix, firstByteUnlimited: true))
        {
            return null;
        }

        int length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length < HsmsHeader.Size)
        {
            throw new SecsException($"HSMS 帧长 {length} 非法");
        }

        var frame = new byte[length];
        return ReadExact(frame, firstByteUnlimited: false) ? frame : null;
    }

    private bool ReadExact(byte[] buffer, bool firstByteUnlimited)
    {
        int done = 0;
        while (done < buffer.Length)
        {
            bool intercharacter = !firstByteUnlimited || done > 0;
            ApplyReadTimeout(intercharacter ? _settings.T8IntercharacterTimeoutMs : Timeout.Infinite);
            int read;
            try
            {
                read = _stream.Read(buffer, done, buffer.Length - done);
            }
            catch (IOException exception) when (exception.InnerException is SocketException
            {
                SocketErrorCode: SocketError.TimedOut
            })
            {
                throw new SecsTimeoutException("T8：帧内字节间超时");
            }

            if (read <= 0)
            {
                return false;
            }

            done += read;
        }
        return true;
    }

    /// <summary>不支持 ReadTimeout 的流（如内存流）自动退化为不限时，只有 NetworkStream 才有 T8 保护。</summary>
    private void ApplyReadTimeout(int milliseconds)
    {
        if (!_readTimeoutSupported)
        {
            return;
        }

        try
        {
            _stream.ReadTimeout = milliseconds;
        }
        catch
        {
            _readTimeoutSupported = false;
        }
    }

    /// <summary>分发一帧（头 + 可选 Item）。订阅方异常只记日志，不拖垮泵。</summary>
    private void Dispatch(byte[] frame)
    {
        var header = HsmsHeader.Parse(frame);
        if (header.SType == 0)
        {
            DispatchData(header, frame.AsSpan(HsmsHeader.Size));
            return;
        }

        _sink.Trace(SecsMessageDirection.Received, new HsmsMessage(header, null));
        switch (header.MessageType)
        {
            case HsmsMessageType.SelectReq:
                // 单会话模式：对端想通讯就接受，回 0（已 SELECTED 时再收一个也回 0，保持链路）
                WriteFrame(HsmsHeader.CreateControl(HsmsMessageType.SelectRsp, header.SystemBytes), null);
                OnSelected();
                break;

            case HsmsMessageType.SelectRsp:
                if (_controls.TryRemove(header.SystemBytes, out var select))
                {
                    if (header.Function == 0)
                    {
                        select.TrySetResult(header);
                    }
                    else
                    {
                        select.TrySetException(new HsmsConnectionException($"对端拒绝 Select，结果码 {header.Function}"));
                    }
                }
                break;

            case HsmsMessageType.DeselectReq:
                WriteFrame(HsmsHeader.CreateControl(HsmsMessageType.DeselectRsp, header.SystemBytes), null);
                Close("对端 Deselect");
                break;

            case HsmsMessageType.DeselectRsp:
                if (_controls.TryRemove(header.SystemBytes, out var deselect))
                {
                    deselect.TrySetResult(header);
                    Close("Deselect 完成");
                }
                break;

            case HsmsMessageType.LinktestReq:
                WriteFrame(HsmsHeader.CreateControl(HsmsMessageType.LinktestRsp, header.SystemBytes), null);
                break;

            case HsmsMessageType.LinktestRsp:
                if (_controls.TryRemove(header.SystemBytes, out var linktest))
                {
                    linktest.TrySetResult(header);
                }
                break;

            case HsmsMessageType.SeparateReq:
                Close("对端 Separate");
                break;

            case HsmsMessageType.RejectReq:
                _sink.Warn(Category, $"对端 Reject: 事务 Sys={header.SystemBytes} 原因码 {header.Function}");
                if (_controls.TryRemove(header.SystemBytes, out var rejected))
                {
                    rejected.TrySetException(new HsmsConnectionException("对端 Reject"));
                }
                break;

            default:
                _sink.Warn(Category, $"不认识的 SType={header.SType}，丢弃");
                break;
        }
    }

    private void DispatchData(HsmsHeader header, ReadOnlySpan<byte> body)
    {
        if (!IsSelected)
        {
            _sink.Warn(Category, $"未 SELECTED 收到数据消息，丢弃: S{header.Stream}F{header.Function}");
            return;
        }

        if (header.PType != 0)
        {
            _sink.Warn(Category, $"PType={header.PType} 不支持（只支持 SECS-II），回 Reject");
            WriteFrame(HsmsHeader.CreateControl(HsmsMessageType.RejectReq, header.SystemBytes, result: 2), null);
            return;
        }

        if (header.DeviceId != _settings.DeviceId)
        {
            _sink.Warn(Category, $"DeviceId={header.DeviceId} 与本端 {_settings.DeviceId} 不符，丢弃");
            return;
        }

        SecsItem? item = null;
        if (!body.IsEmpty)
        {
            try
            {
                item = SecsCodec.Decode(body);
            }
            catch (SecsException exception)
            {
                Close($"报文解码失败: {exception.Message}");
                return;
            }
        }

        var message = new HsmsMessage(header, item);
        _sink.Trace(SecsMessageDirection.Received, message);

        if (_transactions.TryRemove(header.SystemBytes, out var transaction))
        {
            // SystemBytes 对上了：这是我的 primary 的 secondary
            transaction.TrySetResult(message);
            return;
        }

        try
        {
            PrimaryReceived?.Invoke(message);
        }
        catch (Exception exception)
        {
            _sink.Error(Category, $"PrimaryReceived 订阅方抛异常: {exception.Message}");
        }
    }

    #endregion

    #region 状态与生命周期

    /// <summary>主动方的 Select 流程：发 Select.req（T6）等 Select.rsp，成功进 SELECTED，失败断链。</summary>
    private void SelectLoop()
    {
        try
        {
            SendControl(HsmsMessageType.SelectReq, _settings.T6ControlTimeoutMs);
            OnSelected();
        }
        catch (SecsException exception)
        {
            Close($"Select 失败: {exception.Message}");
        }
    }

    /// <summary>被动方的 T7 看门狗：TCP 连上后这么久还没 SELECTED 就断开。</summary>
    private void WatchNotSelected()
    {
        Thread.Sleep(_settings.T7NotSelectedTimeoutMs);
        if (_closed == 0 && _state != HsmsLinkState.Selected)
        {
            Close("T7：连上后未收到 Select");
        }
    }

    private void OnSelected()
    {
        if (_state == HsmsLinkState.Selected)
        {
            return;
        }

        _state = HsmsLinkState.Selected;
        _sink.Info(Category, "SELECTED");
        Selected?.Invoke();

        // 两端都可以发心跳（都必答 Linktest），默认 30 秒一次，互为探活
        if (_settings.LinktestIntervalMs > 0 && Interlocked.Exchange(ref _heartbeatStarted, 1) == 0)
        {
            _ = Task.Factory.StartNew(LinktestLoop, TaskCreationOptions.LongRunning);
        }
    }

    /// <summary>Linktest 心跳：周期发 Linktest.req（T6 等 rsp），失败判链路死并断链。</summary>
    private void LinktestLoop()
    {
        while (_state == HsmsLinkState.Selected)
        {
            Thread.Sleep(_settings.LinktestIntervalMs);
            if (_closed == 1 || _state != HsmsLinkState.Selected)
            {
                return;
            }

            try
            {
                SendControl(HsmsMessageType.LinktestReq, _settings.T6ControlTimeoutMs);
            }
            catch (SecsException exception)
            {
                Close($"Linktest 失败: {exception.Message}");
                return;
            }
        }
    }

    /// <summary>
    /// 关闭连接：幂等。关流（阻塞在 Read 上的泵会立刻被 IOException 打醒）、把在途事务全部打失败
    /// （等回复的 SendAsync 会收到 HsmsConnectionException），最后回调 Closed。
    /// </summary>
    public void Close(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        _state = HsmsLinkState.NotConnected;
        try
        {
            _stream.Close();
        }
        catch
        {
            // 对端可能先关了
        }

        foreach (var transaction in _transactions.Values)
        {
            transaction.TrySetException(new HsmsConnectionException($"连接已断开（{reason}）"));
        }
        _transactions.Clear();

        foreach (var control in _controls.Values)
        {
            control.TrySetException(new HsmsConnectionException($"连接已断开（{reason}）"));
        }
        _controls.Clear();

        _sink.Info(Category, $"连接关闭: {reason}");
        Closed?.Invoke(reason);
    }

    public void Dispose()
    {
        Close("Dispose");
    }

    private uint NextSystemBytes()
    {
        return (uint)Interlocked.Increment(ref _systemBytes);
    }

    #endregion
}
