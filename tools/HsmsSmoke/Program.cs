using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Secs;
using xyz.Secs.Diagnostics;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

// HsmsComponent 冒烟（链路和报文分发，不带 GEM）：组件经 ComponentLoader 按 sc.xml 同款结构装配，登记几个测试处理方，
// 假 EAP 用 HsmsConnector 连入，验证：按 Stream/Function 交给处理方（在派发线程上，可以 await）、处理方抛数据错回 S9F7、
// 抛别的错回 SxF0、没人登记的回 S9F3 / S9F5、闸门挡住的照闸门回、回完再做的事排在回复之后、设备主动发报文等回复、
// 连上 / 断开按先后通知、断线重连后照常分发、Close 发 Separate、跟 gRPC 同端口 / 配置不对 / 端口被占时 Open 只记错误不抛。
// GEM 的答话（S1F13、S1F3……）归 E30，见 EapSmoke。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

var sink = new ConsoleSecsSink();

// 1. 装配：一个 Hsms 节点，时序全部调小，端口避开 5000（Rpc 的 gRPC 在用）。
var port = 5600 + Random.Shared.Next(400);
var node = new ModuleConfig
{
    Name = "Hsms",
    Type = typeof(HsmsComponent).FullName!,
    Values =
    {
        new ValueConfig { Name = "IsEnable", Value = "True" },
        new ValueConfig { Name = "Port", Value = port.ToString() },
        new ValueConfig { Name = "DeviceId", Value = "7" },
        new ValueConfig { Name = "T3ReplyTimeoutMs", Value = "800" },
        new ValueConfig { Name = "T5ConnectRetryMs", Value = "300" },
        new ValueConfig { Name = "T6ControlTimeoutMs", Value = "1000" },
        new ValueConfig { Name = "T7NotSelectedTimeoutMs", Value = "800" },
        new ValueConfig { Name = "T8IntercharacterTimeoutMs", Value = "2000" },
        new ValueConfig { Name = "LinktestIntervalMs", Value = "500" },
    },
};
var roots = ComponentLoader.Load([node]);
var hsms = roots.OfType<HsmsComponent>().Single();
Check(ReferenceEquals(HsmsComponent.Current, hsms), "装配出来即成为 Current");
Check(hsms.IsEnable && hsms.Port == port && hsms.DeviceId == 7, "[SCEditor] 灌值生效（IsEnable/Port/DeviceId）");

// 2. 登记处理方和闸门（链路打开之前）。
var linkEvents = Channel.CreateUnbounded<string>();
hsms.LinkSelected += () => linkEvents.Writer.TryWrite("Selected");
hsms.LinkClosed += reason => linkEvents.Writer.TryWrite("Closed");
hsms.Handle(1, 1, message => SecsReply.Of(SecsItem.L(SecsItem.A("MDLN"), SecsItem.A("REV"))));
hsms.Handle(1, 3, async message =>
{
    await Task.Delay(50);
    throw new SecsException("测试：数据不对");
});
hsms.Handle(2, 1, SecsReply (HsmsMessage message) => throw new InvalidOperationException("测试：处理方自己出错"));
hsms.Handle(6, 15, message => SecsReply.Of(SecsItem.B(0)).Then(() =>
{
    // 回完再做的事：在回复发出去之后发一条 primary，假 EAP 应该先收到回复、后收到它
    _ = hsms.SendAsync(new SecsMessage(6, 11, true, SecsItem.L(SecsItem.U4(1), SecsItem.U4(2), SecsItem.L())));
}));
hsms.Handle(2, 13, message => SecsReply.Of(SecsItem.L()));
hsms.Gate = message => message.Header.Stream == 2 && message.Header.Function == 13 ? SecsReply.Abort : null;
bool duplicate = false;
try
{
    hsms.Handle(1, 1, SecsReply (HsmsMessage message) => SecsReply.None);
}
catch (InvalidOperationException)
{
    duplicate = true;
}

Check(duplicate, "同一个 Stream/Function 登记两次应在开机时抛");

hsms.Open();

// 3. 假 EAP 连入（设备号 7 对上）。
var established = 0;
var ready = new TaskCompletionSource<HsmsSession>(TaskCreationOptions.RunContinuationsAsynchronously);
var eap = new HsmsConnector(new HsmsSettings
{
    IsActive = true,
    Host = "127.0.0.1",
    Port = port,
    DeviceId = 7,
    T3ReplyTimeoutMs = 800,
    T5ConnectRetryMs = 300,
    T6ControlTimeoutMs = 1000,
    T7NotSelectedTimeoutMs = 800,
    T8IntercharacterTimeoutMs = 2000,
    LinktestIntervalMs = 500,
}, sink);
var primaries = Channel.CreateUnbounded<HsmsMessage>();
eap.SessionEstablished += session =>
{
    established++;
    session.PrimaryReceived += message =>
    {
        session.Reply(message, message.CreateReply(SecsItem.B(0)));
        primaries.Writer.TryWrite(message);
    };
    ready.TrySetResult(session);
};
eap.Start();
var session = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(session.IsSelected, "假 EAP 应 SELECTED");
Check(await linkEvents.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)) == "Selected", "连上应通知 LinkSelected");
Check(hsms.IsSelected, "组件看链路已连上");

// 3.1 交给登记的处理方，按处理方给的回复回。
var s1f2 = await session.SendAsync(new SecsMessage(1, 1, true));
Check(s1f2.Name == "S1F2" && s1f2.Body!.Items[0].GetString() == "MDLN" && s1f2.Body.Items[1].GetString() == "REV",
    "S1F1 交给处理方，回 S1F2 带处理方给的体");

// 3.2 出错的几种：处理方说数据不对回 S9F7；处理方自己出错回 S2F0；没人管的 Stream 回 S9F3、有人管的 Stream 没这个 Function 回 S9F5；
//     闸门挡住的照闸门回（S2F13 回 S2F0，处理方不会被叫到）。
foreach (var (request, expected) in new[]
{
    (new SecsMessage(1, 3, true, SecsItem.L()), "S9F7"),
    (new SecsMessage(2, 1, true), "S2F0"),
    (new SecsMessage(7, 1, true, SecsItem.L()), "S9F3"),
    (new SecsMessage(1, 99, true), "S9F5"),
    (new SecsMessage(2, 13, true, SecsItem.L()), "S2F0"),
})
{
    try
    {
        await session.SendAsync(request);
        Check(false, $"{request.Name} 应回 {expected}");
    }
    catch (SecsException exception) when (exception is not SecsTimeoutException)
    {
        Check(exception.Message.Contains(expected, StringComparison.Ordinal), $"{request.Name} 应回 {expected}（实际：{exception.Message}）");
    }
}

// 3.3 回完再做的事排在回复后面：先收到 S6F16，后收到设备发的 S6F11。
var s6f16 = await session.SendAsync(new SecsMessage(6, 15, true, SecsItem.U4(1)));
Check(s6f16.Name == "S6F16", "S6F15 回 S6F16");
var after = await primaries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
Check(after.Name == "S6F11 W", "回完再做的事（发 S6F11）在回复之后到");

// 3.4 设备主动发报文等回复：假 EAP 回 B(0)。
var reply = await hsms.SendAsync(new SecsMessage(5, 1, true, SecsItem.L(SecsItem.B(0x80), SecsItem.U4(1), SecsItem.A("x"))));
Check(reply.Name == "S5F2" && reply.Body!.GetBinary()[0] == 0, "设备发 S5F1 等到 S5F2");
await primaries.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

// 3.5 断线重连：会话 Separate 后先通知断开，T5 重连上再通知连上，新会话照常分发。
session.SendSeparate();
Check(await linkEvents.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)) == "Closed", "断开应通知 LinkClosed");
Check(await linkEvents.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)) == "Selected", "重连上应再通知 LinkSelected");
var session2 = eap.Current;
Check(established == 2 && session2 is not null && session2.IsSelected, "断开后应自动重连");
var again = await session2!.SendAsync(new SecsMessage(1, 1, true));
Check(again.Name == "S1F2", "重连后的新会话照常分发");

// 3.6 没连上时设备发报文：抛连接异常。
hsms.Close();
var closed = await session2.SendAsync(new SecsMessage(1, 1, true)).ContinueWith(task => task.IsFaulted);
Check(closed, "Close 后对端再问应失败（链路已断）");
bool notConnected = false;
try
{
    await hsms.SendAsync(new SecsMessage(6, 11, true));
}
catch (HsmsConnectionException)
{
    notConnected = true;
}

Check(notConnected && !hsms.IsSelected, "链路断了设备发报文应抛连接异常");
eap.Dispose();

// 4. 链路起不来的几种情况：Open 只记错误、不抛，后端照常起，链路状态保持未连接。
// ① 跟 gRPC（Rpc 节点）同端口：不去抢——宿主先开 EAP 链路后起 gRPC，抢了 gRPC 就绑不上、整个后端起不来。
_ = new RpcComponent { Port = port };  // 构造即成为 RpcComponent.Current，宿主从它取 gRPC 端口
var sameAsRpc = new HsmsComponent { IsEnable = true, Port = port, LinktestIntervalMs = 0 };
sameAsRpc.Open();
Check(sameAsRpc.LocalEndpoint is null && sameAsRpc.LinkState == HsmsLinkState.NotConnected, "跟 gRPC 同端口时不监听");
sameAsRpc.Close();
RpcComponent.Current = null;

// ② 配置不对（设备号超 15 位、超时填 0）：同样只记错误。端口给 0，万一没拦住会监听到系统分配的端口，检查就会失败。
foreach (var invalid in new[]
{
    new HsmsComponent { IsEnable = true, Port = 0, DeviceId = 40000 },
    new HsmsComponent { IsEnable = true, Port = 0, T3ReplyTimeoutMs = 0 },
})
{
    invalid.Open();
    Check(invalid.LocalEndpoint is null, $"配置不对时 Open 不抛、不监听（DeviceId={invalid.DeviceId}，T3={invalid.T3ReplyTimeoutMs}）");
    invalid.Close();
}

// ③ 端口被别的程序占着：监听失败。
var occupant = new TcpListener(IPAddress.Loopback, 0);
occupant.Start();
var occupiedPort = ((IPEndPoint)occupant.LocalEndpoint).Port;
var clash = new HsmsComponent { IsEnable = true, Port = occupiedPort, LinktestIntervalMs = 0 };
clash.Open();
Check(clash.LocalEndpoint is null && clash.LinkState == HsmsLinkState.NotConnected, "端口被占时 Open 不抛、不监听");
clash.Close();
occupant.Stop();

// ④ 没启用：不监听。
var disabled = new HsmsComponent { IsEnable = false, Port = 0 };
disabled.Open();
Check(disabled.LocalEndpoint is null, "IsEnable=False 时不监听");

Console.WriteLine($"PASS: {checks} 项检查全部通过");

/// <summary>报文明文打到控制台（复用协议库的格式化）。</summary>
sealed class ConsoleSecsSink : ISecsSink
{
    public void Info(string category, string message) => Console.WriteLine($"[{category}] {message}");

    public void Warn(string category, string message) => Console.WriteLine($"[{category}] WARN {message}");

    public void Error(string category, string message) => Console.WriteLine($"[{category}] ERROR {message}");

    public void Trace(SecsMessageDirection direction, HsmsMessage message)
    {
        var arrow = direction == SecsMessageDirection.Sent ? ">>" : "<<";
        Console.WriteLine($"{arrow} {SecsMessageText.Format(message).Replace("\r", " ").Replace("\n", " | ")}");
    }
}
