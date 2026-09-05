using System.Net;
using System.Net.Sockets;
using System.Text;
using RejeRobotSimulator.Handlers;
using RejeRobotSimulator.Protocol;
using RejeRobotSimulator.State;

namespace RejeRobotSimulator.Server;

/// <summary>
/// Robot 仿真 TCP 服务器
/// 监听上位机连接，接收指令并返回仿真回复
/// </summary>
public class TcpServer
{
    private readonly RobotState _state;
    private readonly QueryHandler _queryHandler;
    private readonly SettingHandler _settingHandler;
    private readonly MotionHandler _motionHandler;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<TcpClient> _clients = new();
    private readonly List<NetworkStream> _streams = new();   // 已连接客户端的流，用于主动推送
    private readonly object _writeLock = new();               // 串行化所有写出（应答 + 推送）

    public int Port { get; set; } = 9000;
    public string BindAddress { get; set; } = "0.0.0.0";
    public bool IsRunning { get; private set; }

    /// <summary>第一次回复（确认）延迟，单位毫秒，默认 50ms（贴近真机毫秒级回包，模拟慢设备时调大）</summary>
    public int AckDelayMs { get; set; } = 50;

    /// <summary>第二次回复（结果）延迟，单位毫秒，默认 50ms</summary>
    public int ResponseDelayMs { get; set; } = 50;

    public event Action<string>? OnLog;
    public event Action<string, string>? OnCommandReceived;   // (clientEndpoint, rawCommand)
    public event Action<string, string>? OnResponseSent;      // (clientEndpoint, response)
    public event Action<string>? OnClientConnected;
    public event Action<string>? OnClientDisconnected;

    public TcpServer(int port = 9000)
    {
        Port = port;
        _state = new RobotState();
        _queryHandler = new QueryHandler(_state);
        _settingHandler = new SettingHandler(_state);
        _motionHandler = new MotionHandler(_state);
    }

    /// <summary>
    /// 获取机器人状态（可用于外部配置）
    /// </summary>
    public RobotState State => _state;

    /// <summary>
    /// 获取动作处理器（可配置延迟和失败率）
    /// </summary>
    public MotionHandler MotionHandler => _motionHandler;

    /// <summary>
    /// 启动服务器
    /// </summary>
    public async Task StartAsync()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Parse(BindAddress), Port);
        _listener.Start();
        IsRunning = true;

        Log($"[Server] 仿真服务器已启动 - {BindAddress}:{Port}");
        Log($"[Server] 等待上位机连接...");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client, _cts.Token));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"[Server] 错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        IsRunning = false;

        lock (_clients)
        {
            foreach (var c in _clients)
            {
                try { c.Close(); } catch { }
            }
            _clients.Clear();
        }
        lock (_streams) { _streams.Clear(); }

        Log("[Server] 服务器已停止");
    }

    /// <summary>
    /// 处理单个客户端连接
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        Log($"[Connect] 客户端已连接: {endpoint}");
        OnClientConnected?.Invoke(endpoint);

        lock (_clients) _clients.Add(client);

        NetworkStream? stream = null;
        try
        {
            stream = client.GetStream();
            lock (_streams) { _streams.Add(stream); }
            var buffer = new byte[4096];
            var sb = new StringBuilder();

            while (!ct.IsCancellationRequested && client.Connected)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead == 0) break; // 客户端断开

                string chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                sb.Append(chunk);

                // 按分号分割完整指令
                string accumulated = sb.ToString();
                int semicolonIndex;
                while ((semicolonIndex = accumulated.IndexOf(';')) >= 0)
                {
                    string rawCmd = accumulated[..(semicolonIndex + 1)];
                    accumulated = accumulated[(semicolonIndex + 1)..];

                    if (string.IsNullOrWhiteSpace(rawCmd)) continue;

                    string raw = rawCmd.Trim();
                    OnCommandReceived?.Invoke(endpoint, raw);

                    // 急停/平稳停走快路: 在读循环内联处理 (效果立即 —— AbortSignal 置位打断在途运动),
                    // 回帧仍按延迟异步发出, 不堵读循环。
                    if (IsImmediateCommand(raw))
                    {
                        var (ack, response) = ProcessCommand(raw);
                        _ = Task.Run(() => SendRepliesAsync(stream, endpoint, ack, response, ct), ct);
                        continue;
                    }

                    // 其余命令 (含耗时的运动指令) 整体丢后台执行并回包,
                    // 读循环立即回到 ReadAsync —— 运动期间急停帧才进得来。
                    _ = Task.Run(async () =>
                    {
                        var (ack, response) = ProcessCommand(raw);
                        await SendRepliesAsync(stream, endpoint, ack, response, ct);
                    }, ct);
                }

                sb.Clear();
                sb.Append(accumulated);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"[Error] {endpoint}: {ex.Message}");
        }
        finally
        {
            if (stream != null) { lock (_streams) { _streams.Remove(stream); } }
            lock (_clients) _clients.Remove(client);
            try { client.Close(); } catch { }
            Log($"[Disconnect] 客户端已断开: {endpoint}");
            OnClientDisconnected?.Invoke(endpoint);
        }
    }

    /// <summary>
    /// 需要立即生效的指令 (急停/平稳停): 在读循环内联处理, 不排队。
    /// </summary>
    private static bool IsImmediateCommand(string raw)
    {
        var parsed = CommandParser.Parse(raw);
        if (parsed == null) return false;
        string cmd = parsed.CommandName.ToUpper();
        return cmd is "SSTOP" or "PSTOP";
    }

    /// <summary>
    /// 按配置延迟依次发出 ACK 与结果帧 (多帧用换行分隔, 逐帧发出)。
    /// Send 内部持 _writeLock, 并发回包任务间字节不交错。
    /// </summary>
    private async Task SendRepliesAsync(NetworkStream stream, string endpoint, string ack, string response, CancellationToken ct)
    {
        try
        {
            // 第一次回复延迟（模拟控制器接收处理时间）
            await Task.Delay(AckDelayMs, ct);
            if (!string.IsNullOrEmpty(ack))
            {
                Send(stream, ack);
                OnResponseSent?.Invoke(endpoint, ack);
            }

            // 第二次回复延迟（模拟控制器执行/查询时间）。
            if (!string.IsNullOrEmpty(response))
            {
                foreach (var frame in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    await Task.Delay(ResponseDelayMs, ct);
                    Send(stream, frame);
                    OnResponseSent?.Invoke(endpoint, frame);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"[Error] 回包失败 {endpoint}: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理一条指令，返回（第一次回复, 第二次回复）。
    /// public 供协议自测直接调用真实路由（不经过 TCP）。
    /// </summary>
    public (string ack, string response) ProcessCommand(string raw)
    {
        string ack = ResponseBuilder.BuildAck();
        string response;

        var parsed = CommandParser.Parse(raw);
        if (parsed == null)
        {
            response = ResponseBuilder.BuildFailure("Unknown", "99990000", "Invalid command format");
            return (ack, response);
        }

        Log($"[Recv] {raw}  ->  [{parsed.CommandName}] args=[{parsed.Args}]");

        string cmd = parsed.CommandName.ToUpper();

        // 路由到对应处理器
        if (IsQueryCommand(cmd))
        {
            response = _queryHandler.Handle(parsed);
        }
        else if (IsSettingCommand(cmd))
        {
            response = _settingHandler.Handle(parsed);
            // 订阅 Wafer 事件后立即补推当前 4 臂在位状态, 让上位机尽快建立物理基线
            if (cmd == "SUBWAFER" && parsed.Args == "1")
            {
                BroadcastCurrentWaferStates();
            }
        }
        else if (IsMotionCommand(cmd))
        {
            response = _motionHandler.Handle(parsed);
        }
        else
        {
            response = ResponseBuilder.BuildFailure(parsed.CommandName, "99990099", "Unknown command");
        }

        return (ack, response);
    }

    private static bool IsQueryCommand(string cmd) => cmd switch
    {
        "STATUS" or "ERROR" or "PROJECT" or "PROGRAM" or
        "PRESSURE" or "ACTIVE" or "QENABLE" or "AXISPOS" or
        "QSPEED" or "DRIVEERROR" or "QOPMODE" or
        "QAWC" or "QAWCD" or "QSUBWAFER" => true,
        _ => false
    };

    private static bool IsSettingCommand(string cmd) => cmd switch
    {
        "RESET" or "PSTOP" or "SSTOP" or "RESUME" or "POWERON" or "POWEROFF" or
        "SPEED" or "ARMDISTANCE" or "UNLOAD" or
        "OPENEMV" or "CLOSEEMV" or
        "OPENSTARTHEART" or "CLOSESTARTHEART" or "QHT" or "HEARTTIME" or
        "AXISWORKHOME" or "SETAXISPOS" or "SETAXISNEG" or "SETAXISV" or
        "DMO" or "DMC" or "AEO" or "AEC" or "SWO" or "SWC" or
        "AXISRANGE" or "ZLS" or "ZFS" or "SAWCD" or "SUBWAFER" => true,
        _ => false
    };

    private static bool IsMotionCommand(string cmd) => cmd switch
    {
        "HOME" or "G" or "GIN" or "GOT" or "GW" or "GWA" or
        "P" or "PIN" or "POT" or "PW" or "PWA" or "GAP" => true,
        _ => false
    };

    /// <summary>
    /// 订阅后立即广播当前 4 臂 wafer 在位状态 (0=有片, 1=无片), 作为上位机物理基线。
    /// </summary>
    private void BroadcastCurrentWaferStates()
    {
        Broadcast(ResponseBuilder.BuildEvent($"SubWaferEx,1,{(_state.Arm1HasWafer ? 0 : 1)}"));
        Broadcast(ResponseBuilder.BuildEvent($"SubWaferEx,2,{(_state.Arm2HasWafer ? 0 : 1)}"));
        Broadcast(ResponseBuilder.BuildEvent($"SubWaferEx,3,{(_state.Arm3HasWafer ? 0 : 1)}"));
        Broadcast(ResponseBuilder.BuildEvent($"SubWaferEx,4,{(_state.Arm4HasWafer ? 0 : 1)}"));
    }

    /// <summary>向单个客户端写出（串行化，避免与主动推送交叉）</summary>
    private void Send(NetworkStream stream, string data)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(data);
        lock (_writeLock)
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
    }

    /// <summary>
    /// 主动推送：向所有已连接客户端广播一帧（用于 SubWaferEx 事件、Error 主动上报、心跳等）。
    /// </summary>
    public void Broadcast(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        List<NetworkStream> snapshot;
        lock (_streams) { snapshot = new List<NetworkStream>(_streams); }
        if (snapshot.Count == 0) return;

        byte[] bytes = Encoding.ASCII.GetBytes(data);
        foreach (var st in snapshot)
        {
            try
            {
                lock (_writeLock)
                {
                    st.Write(bytes, 0, bytes.Length);
                    st.Flush();
                }
            }
            catch { /* 个别客户端写失败忽略，由读循环负责清理 */ }
        }
        OnResponseSent?.Invoke("(推送)", data);
    }

    private void Log(string message)
    {
        OnLog?.Invoke(message);
    }
}
