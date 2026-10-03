using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using xyz.Secs;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

// xyz.Secs 冒烟：SECS-II 编解码（按 E5 手算的固定字节 + round-trip）、消息头固定字节，回环 TCP 上跑 HSMS-SS 全握手
// （Select、S1F13/S1F14 事务、W=0 单发、T3 超时、Linktest、Separate 后 T5 重连、断线中发送、重复接入拒绝、T7），
// 以及端口被占直接报错、设备端回 S9（S9F1/S9F7）、Host 端不发 S9 改回 F0。
// 固定字节不经本库的编码器生成：本库自己编自己解（round-trip）、自己连自己，看不出两头一起错的问题。
// 不连真 EAP，端口用系统分配。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks++;
}

// 裸 TCP 收发一帧（4 字节长度 + 10 字节头 + 体）：直接看本库收发的真实字节，不经本库的编解码。
static async Task WriteRawAsync(NetworkStream stream, string hex)
{
    var bytes = Convert.FromHexString(hex);
    var frame = new byte[4 + bytes.Length];
    BinaryPrimitives.WriteInt32BigEndian(frame, bytes.Length);
    bytes.CopyTo(frame, 4);
    await stream.WriteAsync(frame);
}

static async Task<string> ReadRawAsync(NetworkStream stream)
{
    var prefix = new byte[4];
    await stream.ReadExactlyAsync(prefix).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    var frame = new byte[BinaryPrimitives.ReadInt32BigEndian(prefix)];
    await stream.ReadExactlyAsync(frame).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    return Convert.ToHexString(frame);
}

// 报文和状态直接打到控制台：顺带展示 SecsMessageText 的明文格式。
var sink = new ConsoleSecsSink();

// 1. 编解码 round-trip：字节 → 树 → 字节必须无损。覆盖空 List、嵌套、全格式、
//    长度跨 1 字节/2 字节头的边界（260 字节 A）。
var codecCases = new List<SecsItem>
{
    SecsItem.L(),
    SecsItem.L(SecsItem.L(), SecsItem.A("MDLN"), SecsItem.L(SecsItem.U4(10000), SecsItem.F4(1.5f))),
    SecsItem.A(""),
    SecsItem.A("xyz"),
    SecsItem.A(new string('X', 300)),
    SecsItem.B(),
    SecsItem.B(0x00, 0x01, 0xAB, 0xFF),
    SecsItem.Boolean(),
    SecsItem.Boolean(true, false, true),
    SecsItem.I1(sbyte.MinValue, sbyte.MaxValue),
    SecsItem.I2(short.MinValue, short.MaxValue),
    SecsItem.I4(int.MinValue, int.MaxValue),
    SecsItem.I8(long.MinValue, long.MaxValue),
    SecsItem.U1(byte.MinValue, byte.MaxValue),
    SecsItem.U2(ushort.MinValue, ushort.MaxValue),
    SecsItem.U4(uint.MinValue, uint.MaxValue),
    SecsItem.U8(ulong.MinValue, ulong.MaxValue),
    SecsItem.F4(0.0f, -1.5f, 3.14159f),
    SecsItem.F8(0.0, -1.5e300, 3.141592653589793),
};
foreach (var text in new[] { "MDLN", string.Empty })
{
    codecCases.Add(SecsItem.A(text));
}
var wrapped = codecCases.Select((item, index) => (item, index)).ToList();
foreach (var (item, index) in wrapped)
{
    var bytes = SecsCodec.Encode(item);
    var back = SecsCodec.Decode(bytes);
    Check(back.Equals(item), $"round-trip 应无损（第 {index} 项 {item.Format}，{bytes.Length} 字节）");
    var again = SecsCodec.Encode(back);
    Check(again.SequenceEqual(bytes), "二次编码应与第一次逐位一致");
}

// 1b. 固定字节（SEMI E5）：格式字节 = 格式码（八进制）左移 2 位 + 长度字节数（1~3），长度大端。
var fixtures = new (SecsItem Item, string Hex)[]
{
    (SecsItem.L(), "0100"), (SecsItem.B(0x80), "210180"), (SecsItem.Boolean(true, false), "25020100"),
    (SecsItem.A("A"), "410141"), (SecsItem.J("\u00A1"), "4501A1"),
    (SecsItem.I1(-1), "6501FF"), (SecsItem.I2(-2), "6902FFFE"), (SecsItem.I4(-3), "7104FFFFFFFD"),
    (SecsItem.I8(-1), "6108FFFFFFFFFFFFFFFF"), (SecsItem.U1(2), "A50102"), (SecsItem.U2(256), "A9020100"),
    (SecsItem.U4(1), "B10400000001"), (SecsItem.U8(1), "A1080000000000000001"),
    (SecsItem.F4(1), "91043F800000"), (SecsItem.F8(1), "81083FF0000000000000"),
    (SecsItem.L(SecsItem.U4(1), SecsItem.A("A")), "0102B10400000001410141"),
};
foreach (var (item, hex) in fixtures)
{
    Check(Convert.ToHexString(SecsCodec.Encode(item)) == hex, $"{item.Format} 应编成 {hex}");
    Check(SecsCodec.Decode(Convert.FromHexString(hex)).Equals(item), $"{hex} 应解回 {item.Format}");
}

foreach (var (size, prefix) in new[] { (255, "21FF"), (256, "220100"), (65535, "22FFFF"), (65536, "23010000") })
{
    Check(Convert.ToHexString(SecsCodec.Encode(SecsItem.B(new byte[size]))).StartsWith(prefix, StringComparison.Ordinal),
        $"{size} 字节的长度头应是 {prefix}");
}

foreach (var hex in new[] { "4000", "B103000000", "0102", "4101FF", "41014100", "FF00" })
{
    try
    {
        SecsCodec.Decode(Convert.FromHexString(hex));
        Check(false, $"非法字节 {hex} 应解码失败");
    }
    catch (SecsException)
    {
        Check(true, $"非法字节 {hex} 被拒绝");
    }
}

try
{
    SecsCodec.Encode(SecsItem.A("中文"));
    Check(false, "A 类型里有中文应报错");
}
catch (SecsException)
{
    Check(true, "A 类型里的非 ASCII 字符直接报错，不悄悄变成问号");
}

// 2. 消息头（E37）：W-Bit 在 Stream 字节最高位，Function 占满 8 位；控制消息的 SessionID 是 FFFF。
Span<byte> headerBuffer = stackalloc byte[10];
var header = HsmsHeader.CreateData(0x1234, 5, 13, true, 0xDEAD_BEEF);
header.Write(headerBuffer);
Check(Convert.ToHexString(headerBuffer) == "1234850D0000DEADBEEF", "数据消息头应是 1234 85 0D 00 00 DEADBEEF");
var parsedHeader = HsmsHeader.Parse(headerBuffer);
Check(parsedHeader.DeviceId == 0x1234 && parsedHeader.Stream == 5 && parsedHeader.Function == 13
      && parsedHeader.ReplyExpected && parsedHeader.SystemBytes == 0xDEAD_BEEF, "消息头 round-trip 应无损");
HsmsHeader.CreateControl(HsmsMessageType.SelectReq, 1).Write(headerBuffer);
Check(Convert.ToHexString(headerBuffer) == "FFFF0000000100000001", "Select.req 头应是 FFFF 00 00 00 01 00000001");

// 3. 回环集成：设备端被动监听（端口系统分配），EAP 端主动连出。时序全部调小让冒烟几秒内跑完。
var deviceSettings = new HsmsSettings
{
    IsEquipment = true,
    IsActive = false,
    Port = 0,
    DeviceId = 0,
    T3ReplyTimeoutMs = 800,
    T5ConnectRetryMs = 300,
    T6ControlTimeoutMs = 1000,
    T7NotSelectedTimeoutMs = 800,
    T8IntercharacterTimeoutMs = 2000,
    LinktestIntervalMs = 250,
};
var listener = new HsmsListener(deviceSettings, sink);
var deviceReady = new TaskCompletionSource<HsmsSession>(TaskCreationOptions.RunContinuationsAsynchronously);
var w0Seen = new TaskCompletionSource<HsmsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
listener.SessionEstablished += session =>
{
    deviceReady.TrySetResult(session);
    session.PrimaryReceived += message =>
    {
        switch (message.Name)
        {
            case "S1F13 W":
                session.Reply(message, message.CreateReply(
                    SecsItem.L(SecsItem.B(0), SecsItem.L(SecsItem.A("xyz"), SecsItem.A("1.0.0")))));
                break;

            case "S1F1 W":
                session.Reply(message, message.CreateReply(SecsItem.L(SecsItem.A("xyz"), SecsItem.A("1.0.0"))));
                break;

            // S2F31 故意不回：给 T3 超时场景当靶子
            case "S6F11":
                w0Seen.TrySetResult(message);
                break;
        }
    };
};
listener.Start();
Check(listener.LocalEndpoint is not null, "监听应拿到系统分配的端口");
var port = listener.LocalEndpoint!.Port;

var hostSettings = new HsmsSettings
{
    IsActive = true,
    Host = "127.0.0.1",
    Port = port,
    DeviceId = 0,
    T3ReplyTimeoutMs = 800,
    T5ConnectRetryMs = 300,
    T6ControlTimeoutMs = 1000,
    T7NotSelectedTimeoutMs = 800,
    T8IntercharacterTimeoutMs = 2000,
    LinktestIntervalMs = 250,
};
var connector = new HsmsConnector(hostSettings, sink);
var establishedCount = 0;
var hostReady = new TaskCompletionSource<HsmsSession>(TaskCreationOptions.RunContinuationsAsynchronously);
connector.SessionEstablished += session =>
{
    establishedCount++;
    hostReady.TrySetResult(session);
};
connector.Start();
var host = await hostReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
var listenerSession = listener.Current;
Check(host.IsSelected && listenerSession is not null && listenerSession.IsSelected, "两端都应 SELECTED");
// 设备端是先写 Select.rsp 上网络、再进 SELECTED 状态：主机端此刻可能已选中而设备事件还没跑到，
// 用等待断言而不是瞬时 IsCompleted（否则是竞态）。
var device = await deviceReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(device.IsSelected && ReferenceEquals(device, listener.Current), "设备端应拿到会话");

// 3.1 数据事务：S1F13 带体发过去，S1F14 带体回回来，事务号原样带回。
var s1f13 = new SecsMessage(1, 13, true, SecsItem.L(SecsItem.A("MDLN"), SecsItem.A("1.0.0")));
var s1f14 = await host.SendAsync(s1f13);
Check(s1f14.Name == "S1F14" && !s1f14.Header.ReplyExpected, "回的应是 S1F14（secondary 无 W）");
Check(s1f14.Header.SystemBytes == s1f13.SystemBytes, "secondary 应带回 primary 的事务号");
Check(s1f14.Body!.Items.Count == 2 && s1f14.Body.Items[0].GetBinary()[0] == 0
      && s1f14.Body.Items[1].Items[0].GetString() == "xyz", "S1F14 应带回通讯建立码与 MDLN/SOFTREV");

// 3.2 空 body 消息（S1F1 Are You There）。
var s1f2 = await host.SendAsync(new SecsMessage(1, 1, true));
Check(s1f2.Name == "S1F2" && s1f2.Body is not null && s1f2.Body.Items.Count == 2, "S1F1 应答出 S1F2");

// 3.3 W=0 单发：不等回复，设备端事件里能看到。
host.Send(new SecsMessage(6, 11, false, SecsItem.L(SecsItem.U4(70001))));
var w0Message = await w0Seen.Task.WaitAsync(TimeSpan.FromSeconds(3));
Check(w0Message.Body!.First().GetUInt64() == 70001, "设备端应收到 S6F11 事件报文");
try
{
    host.Send(new SecsMessage(1, 1));  // W=0 构造默认值就是 false，这里走 Send 是正常路径
    await host.SendAsync(new SecsMessage(6, 12, false));
    Check(false, "W=0 的消息 SendAsync 应该被挡");
}
catch (SecsException)
{
    Check(true, "W=0 的消息 SendAsync 被正确拒绝");
}

// 3.4 T3 超时：S2F31 没人回，800ms 后抛 SecsTimeoutException，且不影响链路。
try
{
    await host.SendAsync(new SecsMessage(2, 31, true, SecsItem.A("20260101120000")));
    Check(false, "无人应答应 T3 超时");
}
catch (SecsTimeoutException)
{
    Check(true, "T3 超时被抛出");
}
var afterTimeout = await host.SendAsync(new SecsMessage(1, 1, true));
Check(afterTimeout.Name == "S1F2", "T3 超时后链路应仍可用");

// 3.5 Linktest：双端 250ms 心跳跑一阵子，链路保持。
await Task.Delay(1200);
listenerSession = listener.Current;
Check(host.IsSelected && listenerSession is not null && listenerSession.IsSelected, "心跳期间链路应保持 SELECTED");

// 3.6 重复接入拒绝：链路活着时第二条连接应被立刻关掉，原链路不受影响。
var rogue = new TcpClient();
await rogue.ConnectAsync("127.0.0.1", port);
var rogueClosed = await rogue.GetStream().ReadAsync(new byte[16]);
Check(rogueClosed == 0, "重复接入的连接应被关闭");
Check(host.IsSelected, "拒绝重复接入不应影响原链路");

// 3.7 T7：连上不发 Select，设备端 800ms 后应主动断开。
var silent = new TcpClient();
await silent.ConnectAsync("127.0.0.1", port);
var silentClosed = await silent.GetStream().ReadAsync(new byte[16]);
Check(silentClosed == 0, "不 Select 的连接应被 T7 踢掉");

// 3.8 Separate 后 T5 重连：老会话句柄作废，Connector 自动建新会话。
var deadHost = host;
deadHost.SendSeparate();
var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
while (establishedCount < 2 && DateTime.UtcNow < deadline)
{
    await Task.Delay(50);
}
Check(establishedCount == 2, "断开后应在 T5 间隔内自动重连");
var newHost = connector.Current;
Check(newHost is not null && newHost.IsSelected && !ReferenceEquals(newHost, deadHost), "重连后应有新的 SELECTED 会话");
var reconnectReply = await newHost!.SendAsync(new SecsMessage(1, 1, true));
Check(reconnectReply.Name == "S1F2", "重连后的新会话应能正常问答");

// 3.9 断线中发送：作废会话上发消息应抛 HsmsConnectionException。
try
{
    await deadHost.SendAsync(new SecsMessage(1, 13, true));
    Check(false, "作废会话发送应抛连接异常");
}
catch (HsmsConnectionException)
{
    Check(true, "作废会话发送被正确拒绝");
}

connector.Dispose();
listener.Dispose();

// 4. 端口被占：别的程序占着 127.0.0.1 的同一端口（本框架 gRPC 就是 localhost:5000），监听直接报错；换端口后能再起。
var occupant = new TcpListener(IPAddress.Loopback, 0);
occupant.Start();
var occupiedPort = ((IPEndPoint)occupant.LocalEndpoint).Port;
var clashSettings = new HsmsSettings { IsEquipment = true, Port = occupiedPort, LinktestIntervalMs = 0 };
using (var clash = new HsmsListener(clashSettings, sink))
{
    try
    {
        clash.Start();
        Check(false, $"端口 {occupiedPort} 已被占用，监听应失败");
    }
    catch (HsmsConnectionException)
    {
        Check(true, "端口被占时监听直接报错");
    }

    clashSettings.Port = 0;
    clash.Start();
    Check(clash.LocalEndpoint is not null && clash.LocalEndpoint.Port > 0 && clash.LocalEndpoint.Port != occupiedPort,
        "换端口后同一个监听能再起来");
}
occupant.Stop();

// 5. 设备号对不上：设备端回 S9F1，Host 端在途事务立刻失败，不用干等 T3。
var mismatchDevice = new HsmsListener(new HsmsSettings { IsEquipment = true, Port = 0, DeviceId = 0, LinktestIntervalMs = 0 }, sink);
mismatchDevice.Start();
var mismatchHost = new HsmsConnector(new HsmsSettings
{
    IsActive = true, Port = mismatchDevice.LocalEndpoint!.Port, DeviceId = 1, T3ReplyTimeoutMs = 5000, LinktestIntervalMs = 0,
}, sink);
var mismatchReady = new TaskCompletionSource<HsmsSession>(TaskCreationOptions.RunContinuationsAsynchronously);
mismatchHost.SessionEstablished += session => mismatchReady.TrySetResult(session);
mismatchHost.Start();
var mismatchSession = await mismatchReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
var watch = Stopwatch.StartNew();
try
{
    await mismatchSession.SendAsync(new SecsMessage(1, 1, true));
    Check(false, "设备号对不上应收到 S9F1");
}
catch (SecsException exception) when (exception is not SecsTimeoutException)
{
    Check(exception.Message.Contains("S9F1", StringComparison.Ordinal) && watch.ElapsedMilliseconds < 2000,
        $"设备号对不上应立刻收到 S9F1（实际：{exception.Message}，{watch.ElapsedMilliseconds} ms）");
}
mismatchHost.Dispose();
mismatchDevice.Dispose();

// 6. 裸 TCP 冒充 Host 看设备端发出的字节：解不开的报文回 S9F7，自己发的 primary 等不到回复回 S9F9，体都是原始消息头。
var rawHostTarget = new HsmsListener(new HsmsSettings
{
    IsEquipment = true, Port = 0, DeviceId = 0, T3ReplyTimeoutMs = 500, LinktestIntervalMs = 0,
}, sink);
var rawHostTargetReady = new TaskCompletionSource<HsmsSession>(TaskCreationOptions.RunContinuationsAsynchronously);
rawHostTarget.SessionEstablished += session => rawHostTargetReady.TrySetResult(session);
rawHostTarget.Start();
using (var rawHost = new TcpClient())
{
    await rawHost.ConnectAsync(IPAddress.Loopback, rawHostTarget.LocalEndpoint!.Port);
    var stream = rawHost.GetStream();
    await WriteRawAsync(stream, "FFFF0000000100000001");
    Check(await ReadRawAsync(stream) == "FFFF0000000200000001", "Select.rsp 应带回事务号、结果码 0");
    var equipment = await rawHostTargetReady.Task.WaitAsync(TimeSpan.FromSeconds(3));

    await WriteRawAsync(stream, "00008101000000000077" + "B103000000");
    var s9f7 = await ReadRawAsync(stream);
    Check(s9f7.StartsWith("000009070000", StringComparison.Ordinal) && s9f7[20..] == "210A00008101000000000077",
        $"解不开的报文应回 S9F7 + 原始头（实际 {s9f7}）");

    var pending = equipment.SendAsync(new SecsMessage(1, 1, true));
    var primary = await ReadRawAsync(stream);
    Check(primary.Length == 20 && primary.StartsWith("000081010000", StringComparison.Ordinal), $"设备端发的 S1F1 W 头不对：{primary}");
    var s9f9 = await ReadRawAsync(stream);
    Check(s9f9.StartsWith("000009090000", StringComparison.Ordinal) && s9f9[20..] == "210A" + primary,
        $"T3 到期应发 S9F9 + 原事务头（实际 {s9f9}）");
    try
    {
        await pending;
        Check(false, "没人回应 T3 超时");
    }
    catch (SecsTimeoutException)
    {
        Check(true, "设备端 T3 超时照常抛出");
    }
}
rawHostTarget.Dispose();

// 7. 裸 TCP 冒充设备看 Host 端：Host 不发 S9，解不开、设备号不对的 W 报文回同 Stream 的 F0（带回原设备号和事务号）；
//    设备回的 S9 让 Host 的在途事务立刻失败。
var rawEquipment = new TcpListener(IPAddress.Loopback, 0);
rawEquipment.Start();
var f0Host = new HsmsConnector(new HsmsSettings
{
    IsActive = true, Port = ((IPEndPoint)rawEquipment.LocalEndpoint).Port, DeviceId = 0, T3ReplyTimeoutMs = 5000, LinktestIntervalMs = 0,
}, sink);
var f0Ready = new TaskCompletionSource<HsmsSession>(TaskCreationOptions.RunContinuationsAsynchronously);
f0Host.SessionEstablished += session => f0Ready.TrySetResult(session);
f0Host.Start();
using (var rawDevice = await rawEquipment.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3)))
{
    var stream = rawDevice.GetStream();
    var select = await ReadRawAsync(stream);
    Check(select.Length == 20 && select.StartsWith("FFFF00000001", StringComparison.Ordinal), $"Host 应先发 Select.req（实际 {select}）");
    await WriteRawAsync(stream, "FFFF00000002" + select[12..]);
    var f0Session = await f0Ready.Task.WaitAsync(TimeSpan.FromSeconds(3));

    await WriteRawAsync(stream, "00008101000000000088" + "B103000000");
    Check(await ReadRawAsync(stream) == "00000100000000000088", "Host 收到解不开的 W 报文应回 S1F0，不发 S9");
    await WriteRawAsync(stream, "00058101000000000089");
    Check(await ReadRawAsync(stream) == "00050100000000000089", "设备号不对的 W 报文也回 S1F0，带回对方的设备号");

    var pending = f0Session.SendAsync(new SecsMessage(1, 3, true, SecsItem.L()));
    var request = await ReadRawAsync(stream);
    await WriteRawAsync(stream, "0000090500000000000A" + "210A" + request[..20]);
    watch.Restart();
    try
    {
        await pending;
        Check(false, "设备回了 S9F5，Host 的事务应失败");
    }
    catch (SecsException exception) when (exception is not SecsTimeoutException)
    {
        Check(exception.Message.Contains("S9F5", StringComparison.Ordinal) && watch.ElapsedMilliseconds < 2000,
            $"设备回 S9F5 后 Host 的事务应立刻失败（实际：{exception.Message}）");
    }
}
f0Host.Dispose();
rawEquipment.Stop();

Console.WriteLine($"PASS: {checks} 项检查全部通过");

/// <summary>把库里的日志与报文明文打到控制台。</summary>
sealed class ConsoleSecsSink : ISecsSink
{
    public void Info(string category, string message) => Console.WriteLine($"[{category}] {message}");

    public void Warn(string category, string message) => Console.WriteLine($"[{category}] WARN {message}");

    public void Error(string category, string message) => Console.WriteLine($"[{category}] ERROR {message}");

    public void Trace(SecsMessageDirection direction, HsmsMessage message)
    {
        var arrow = direction == SecsMessageDirection.Sent ? ">>" : "<<";
        Console.WriteLine($"{arrow} {SecsMessageText.Format(message)}");
    }
}
