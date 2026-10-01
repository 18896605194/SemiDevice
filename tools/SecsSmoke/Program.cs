using System.Net.Sockets;
using xyz.Secs;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

// xyz.Secs 冒烟：SECS-II 编解码 round-trip + 回环 TCP 上跑 HSMS-SS 全握手
// （Select、S1F13/S1F14 事务、W=0 单发、T3 超时、Linktest、Separate 后 T5 重连、断线中发送、重复接入拒绝、T7）。
// 不连真 EAP，端口用系统分配。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks++;
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

// 2. 消息头 round-trip：W-Bit 在 Function 字节最高位，字段全保真。
Span<byte> headerBuffer = stackalloc byte[10];
var header = HsmsHeader.CreateData(0x1234, 5, 13, true, 0xDEAD_BEEF);
header.Write(headerBuffer);
var parsedHeader = HsmsHeader.Parse(headerBuffer);
Check(parsedHeader.DeviceId == 0x1234 && parsedHeader.Stream == 5 && parsedHeader.Function == 13
      && parsedHeader.ReplyExpected && parsedHeader.SystemBytes == 0xDEAD_BEEF, "消息头 round-trip 应无损");
Check((headerBuffer[3] & 0x8D) == 0x8D, "W=1 时 Function 字节最高位应置位");

// 3. 回环集成：设备端被动监听（端口系统分配），EAP 端主动连出。时序全部调小让冒烟几秒内跑完。
var deviceSettings = new HsmsSettings
{
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
                session.Reply(message, message.CreateReply(SecsItem.B(0)));
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
Check(host.IsSelected && listener.Current is { IsSelected: true }, "两端都应 SELECTED");
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
Check(s1f2.Name == "S1F2" && s1f2.Body is not null && s1f2.Body.GetBinary()[0] == 0, "S1F1 应答出 S1F2");

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
Check(host.IsSelected && listener.Current is { IsSelected: true }, "心跳期间链路应保持 SELECTED");

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
