using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using ProtoBuf.Grpc.Server;
using xyz.Client.Common.Events;
using xyz.Client.Common.Rpc;
using xyz.Common.Events;
using xyz.Service.Events;
using xyz.Tools;

// 事件总线冒烟：进程内 EventBus、跨进程 RemoteEventPublisher / RemoteEventSubscriber 各管各的，互不相通
//   dotnet run --project tools\EventBusSmoke                 全部检查（跨进程那段自己起后端子进程）
//   dotnet run --project tools\EventBusSmoke -- server 端口   只起事件后端（给上面那段用，也能手动起来看）
if (args.Length > 1 && args[0] == "server")
{
    await RunServer(int.Parse(args[1]));
    return 0;
}

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    checks++;
}

LocalBus();
RemoteBusAlone();
CrossProcess();

Console.WriteLine($"PASS: {checks} event bus checks (进程内 EventBus、RemoteEventPublisher / RemoteEventSubscriber 的发和收、黑板、死信、跨进程推送、断线重连后重放)");
return 0;

// 进程内：同步、原对象、按 token 分、没有黑板、退订、出错隔离、不出进程
void LocalBus()
{
    #region 发和收：同步、原对象、按 token 分

    var received = new List<LocalMessage>();
    var threads = new List<int>();
    var original = new LocalMessage { Text = "a" };
    EventBus.Send(original, "nobody");
    var subscription = EventBus.Register<LocalMessage>("A", message =>
    {
        received.Add(message);
        threads.Add(Environment.CurrentManagedThreadId);
    });
    EventBus.Send(original, "A");
    Check(received.Count == 1 && ReferenceEquals(received[0], original), "进程内：Send 返回前就调完订阅者，传的是原对象");
    Check(threads[0] == Environment.CurrentManagedThreadId, "进程内：在发送方的线程上回调");
    EventBus.Send(new LocalMessage { Text = "b" }, "B");
    EventBus.Send(new LocalMessage { Text = "c" });
    Check(received.Count == 1, "进程内：别的 token、全局消息都不串到 A");
    subscription.Dispose();
    subscription.Dispose();
    EventBus.Send(new LocalMessage { Text = "d" }, "A");
    Check(received.Count == 1, "进程内：退订以后收不到，重复退订不出错");

    var global = new List<LocalMessage>();
    var globalSubscription = EventBus.Register<LocalMessage>(global.Add);
    EventBus.Send(new LocalMessage { Text = "g" });
    EventBus.Send(new LocalMessage { Text = "x" }, "A");
    globalSubscription.Dispose();
    Check(global.Count == 1 && global[0].Text == "g", "进程内：全局订阅只收不带 token 的");

    #endregion

    #region 没有黑板：订阅之前发的收不到

    EventBus.Send(new LocalMessage { Text = "early" }, "late");
    var late = new List<LocalMessage>();
    var lateSubscription = EventBus.Register<LocalMessage>("late", late.Add);
    lateSubscription.Dispose();
    Check(late.Count == 0, "进程内：没有黑板，订阅之前发的收不到");

    #endregion

    #region 出错和退订：一个订阅者抛异常进死信、不耽误后面的；回调里退订自己

    var dead = new List<object>();
    void OnDead(object message, Exception error)
    {
        dead.Add(message);
    }

    EventBus.DeadLetter += OnDead;
    var afterThrow = new List<LocalMessage>();
    var boom = new LocalMessage { Text = "boom" };
    var throwing = EventBus.Register<LocalMessage>("throw", _ => throw new InvalidOperationException("订阅者出错"));
    var next = EventBus.Register<LocalMessage>("throw", afterThrow.Add);
    EventBus.Send(boom, "throw");
    throwing.Dispose();
    next.Dispose();
    EventBus.DeadLetter -= OnDead;
    Check(afterThrow.Count == 1, "进程内：一个订阅者抛异常不耽误后面的，也不抛给发送方");
    Check(dead.Count == 1 && ReferenceEquals(dead[0], boom), "进程内：抛的异常进死信，带上那条消息");

    IDisposable? self = null;
    var selfCalls = 0;
    var otherCalls = 0;
    self = EventBus.Register<LocalMessage>("self", _ =>
    {
        selfCalls++;
        self?.Dispose();
    });
    var other = EventBus.Register<LocalMessage>("self", _ => otherCalls++);
    EventBus.Send(new LocalMessage(), "self");
    EventBus.Send(new LocalMessage(), "self");
    other.Dispose();
    Check(selfCalls == 1 && otherCalls == 2, "进程内：回调里退订自己不出错，这一轮照样发给别人");

    #endregion

    #region 不出进程：EventBus 发的不交给跨进程的线

    var sent = new List<EventMessage>();
    var listener = RemoteEventPublisher.SubscribeSent(sent.Add);
    EventBus.Send(new LocalMessage { Text = "local only" }, "A");
    listener.Dispose();
    Check(sent.Count == 0, "进程内：EventBus 发的不会交给跨进程的线");

    #endregion
}

// RemoteEventPublisher / RemoteEventSubscriber 本身（不走网络）：发只交给线、信封、后端黑板；收派发副本、补发、通知类擦格子、死信
void RemoteBusAlone()
{
    #region 发：只交给线，不在本进程派发

    var sent = new List<EventMessage>();
    var remote = new List<TickMessage>();
    var local = new List<TickMessage>();
    var listener = RemoteEventPublisher.SubscribeSent(sent.Add);
    var remotePage = RemoteEventSubscriber.Register<TickMessage>("r1", remote.Add);
    var localSubscription = EventBus.Register<TickMessage>("r1", local.Add);
    RemoteEventPublisher.Send(new TickMessage { Sequence = 1, Token = "r1" }, "r1");
    listener.Dispose();
    remotePage.Dispose();
    localSubscription.Dispose();
    Check(sent.Count == 1, "跨进程：Send 把信封交给线（后端的 EventService）");
    Check(remote.Count == 0 && local.Count == 0, "跨进程：Send 不在本进程派发，两条总线的订阅者都收不到");
    var payload = JsonHelper.Deserialize<TickMessage>(sent[0].Payload);
    Check(sent[0].TypeName == typeof(TickMessage).FullName && sent[0].Token == "r1" && sent[0].Retain
          && payload is not null && payload.Sequence == 1,
        "跨进程：信封带类型全名、token、写黑板标记和 JSON");

    #endregion

    #region 后端黑板：同一格只留最后一条；通知类不写，还会擦掉同一格

    RemoteEventPublisher.Send(new TickMessage { Sequence = 1, Token = "r2" }, "r2");
    RemoteEventPublisher.Send(new TickMessage { Sequence = 2, Token = "r2" }, "r2");
    RemoteEventPublisher.Send(new TickMessage { Sequence = 3, Token = "r2n" }, "r2n", retain: false);
    var board = RemoteEventPublisher.GetRetained().Where(item => item.Token == "r2" || item.Token == "r2n").ToList();
    var last = board.Count == 1 ? JsonHelper.Deserialize<TickMessage>(board[0].Payload) : null;
    Check(board.Count == 1 && board[0].Token == "r2" && last is not null && last.Sequence == 2, "黑板：同一格只留最后一条，通知类不写");
    RemoteEventPublisher.Send(new TickMessage { Sequence = 4, Token = "r2" }, "r2", retain: false);
    Check(RemoteEventPublisher.GetRetained().All(item => item.Token != "r2"), "黑板：同一格发一条通知类，旧状态擦掉");

    #endregion

    #region 收：Deliver 进来派发副本；黑板补发；通知类照发但擦格子；空 token 当全局

    var received = new List<TickMessage>();
    var page = RemoteEventSubscriber.Register<TickMessage>("r3", received.Add);
    var original = new TickMessage { Sequence = 7, Token = "r3" };
    RemoteEventSubscriber.Deliver(Envelope(original, "r3", retain: true));
    Check(received.Count == 1 && received[0].Sequence == 7 && !ReferenceEquals(received[0], original), "收：按 token 调订阅者，拿到的是拆包出来的副本");
    var late = new List<TickMessage>();
    var latePage = RemoteEventSubscriber.Register<TickMessage>("r3", late.Add);
    Check(late.Count == 1 && late[0].Sequence == 7, "收：黑板上有的，页面后订阅马上补发最后一条");
    RemoteEventSubscriber.Deliver(Envelope(new TickMessage { Sequence = 8, Token = "r3" }, "r3", retain: false));
    var later = new List<TickMessage>();
    var laterPage = RemoteEventSubscriber.Register<TickMessage>("r3", later.Add);
    page.Dispose();
    latePage.Dispose();
    laterPage.Dispose();
    Check(received.Count == 2 && late.Count == 2 && later.Count == 0, "收：通知类照样派发，不写黑板，同一格的旧状态擦掉");

    var global = new List<TickMessage>();
    var globalPage = RemoteEventSubscriber.Register<TickMessage>(global.Add);
    RemoteEventSubscriber.Deliver(Envelope(new TickMessage { Sequence = 9 }, null, retain: false));
    globalPage.Dispose();
    Check(global.Count == 1 && global[0].Sequence == 9, "收：protobuf 省掉的空 token 当全局消息");

    #endregion

    #region 死信：没人订的类型丢掉；解不开、订阅者抛异常进死信，不往外抛

    var dead = new List<EventMessage>();
    void OnDead(EventMessage message, Exception error)
    {
        dead.Add(message);
    }

    RemoteEventSubscriber.DeadLetter += OnDead;
    RemoteEventSubscriber.Deliver(new EventMessage { TypeName = "Nobody.Knows", Token = "r5", Payload = "{}" });
    Check(dead.Count == 0, "死信：没人订的类型直接丢掉，不算死信");
    var badPage = RemoteEventSubscriber.Register<TickMessage>("r5", _ => { });
    RemoteEventSubscriber.Deliver(new EventMessage { TypeName = typeof(TickMessage).FullName!, Token = "r5", Payload = "不是 JSON" });
    badPage.Dispose();
    Check(dead.Count == 1, "死信：解不开的消息进死信，不往外抛");
    var good = new List<TickMessage>();
    var throwing = RemoteEventSubscriber.Register<TickMessage>("r6", _ => throw new InvalidOperationException("页面出错"));
    var goodPage = RemoteEventSubscriber.Register<TickMessage>("r6", good.Add);
    RemoteEventSubscriber.Deliver(Envelope(new TickMessage { Sequence = 1, Token = "r6" }, "r6", retain: false));
    throwing.Dispose();
    goodPage.Dispose();
    RemoteEventSubscriber.DeadLetter -= OnDead;
    Check(good.Count == 1 && dead.Count == 2, "死信：一个页面抛异常进死信，不耽误后面的");

    #endregion

    #region 线：退订以后不再拿到新发的

    var after = new List<EventMessage>();
    var afterListener = RemoteEventPublisher.SubscribeSent(after.Add);
    afterListener.Dispose();
    afterListener.Dispose();
    RemoteEventPublisher.Send(new TickMessage(), "r7", retain: false);
    Check(after.Count == 0, "线：退订以后不再拿到新发的，重复退订不出错");

    #endregion
}

// 跨进程：起后端子进程，客户端照真实的走 RemoteEventConnection（控制台没有界面线程，回调在接收线程上）
void CrossProcess()
{
    var port = FreePort();
    var ticks = new ConcurrentQueue<TickMessage>();
    var lives = new ConcurrentQueue<TickMessage>();
    var notices = new ConcurrentQueue<TickMessage>();
    var changes = new ConcurrentQueue<bool>();
    using var tickPage = RemoteEventSubscriber.Register<TickMessage>("tick", ticks.Enqueue);
    using var livePage = RemoteEventSubscriber.Register<TickMessage>("live", lives.Enqueue);
    using var noticePage = RemoteEventSubscriber.Register<TickMessage>("notice", notices.Enqueue);
    RemoteEventConnection.ConnectionChanged += changes.Enqueue;

    GrpcClientFactory.Initialize($"http://localhost:{port}");
    RemoteEventConnection.Initialize();
    var server = StartServer(port);
    try
    {
        #region 连上：先重放黑板；连之前的通知类、后端进程内发的都不过来

        Check(WaitUntil(() => RemoteEventConnection.IsConnected, 20000), "跨进程：连上后端（收到开流标记才算）");
        Check(WaitUntil(() => lives.Count >= 3 && notices.Count >= 3, 10000), "跨进程：连着时后端新发的状态类、通知类都推过来");
        Check(ticks.Count == 1 && ticks.First().Sequence == 0, "跨进程：连上先重放黑板（连之前发的状态类），后端进程内 EventBus 发的不过来");
        Check(notices.All(item => item.Sequence > 0), "跨进程：连之前发的通知类不补发");
        Check(NotDescending(lives) && NotDescending(notices), "跨进程：按发的顺序到（刚连上时同一条状态可能重放两次，不会倒序）");
        Check(lives.All(item => item.Token == "live") && notices.All(item => item.Token == "notice"), "跨进程：按 token 分开，不串");

        var lateTicks = new List<TickMessage>();
        var lateTickPage = RemoteEventSubscriber.Register<TickMessage>("tick", lateTicks.Add);
        lateTickPage.Dispose();
        Check(lateTicks.Count == 1 && lateTicks[0].Sequence == 0, "跨进程：页面后订阅，客户端黑板马上补发");

        #endregion

        #region 断线重连：后端停了报断开；回来以后 3 秒一次重连上，再重放一遍黑板

        server.Kill(entireProcessTree: true);
        server.WaitForExit();
        Check(WaitUntil(() => !RemoteEventConnection.IsConnected, 10000), "跨进程：后端停了，连接状态变成断开");
        ticks.Clear();
        var stopped = server;
        server = StartServer(port);
        stopped.Dispose();
        Check(WaitUntil(() => RemoteEventConnection.IsConnected, 20000), "跨进程：后端回来，按 3 秒一次重连上");
        Check(WaitUntil(() => !ticks.IsEmpty, 5000) && ticks.First().Sequence == 0, "跨进程：重连上再重放一遍黑板");
        Check(changes.SequenceEqual(new[] { true, false, true }), "跨进程：连接状态按连上、断开、再连上报");

        #endregion
    }
    finally
    {
        if (!server.HasExited)
        {
            server.Kill(entireProcessTree: true);
        }

        server.Dispose();
    }
}

// 模拟从网络上收到的信封（token 传 null 就是 protobuf 把空串省掉了）
static EventMessage Envelope(TickMessage message, string? token, bool retain)
{
    return new EventMessage
    {
        TypeName = typeof(TickMessage).FullName!,
        Token = token!,
        Payload = JsonHelper.Serialize(message),
        Retain = retain,
    };
}

// 起后端子进程：就是自己这个程序带上 server 参数
static Process StartServer(int port)
{
    var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("拿不到自己的程序路径");
    var start = new ProcessStartInfo(processPath) { UseShellExecute = false };
    if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
    {
        start.ArgumentList.Add(typeof(TickMessage).Assembly.Location);
    }

    start.ArgumentList.Add("server");
    start.ArgumentList.Add(port.ToString());
    return Process.Start(start) ?? throw new InvalidOperationException("后端子进程起不来");
}

// 找一个空闲端口（不占着，留给后端子进程监听）
static int FreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static bool WaitUntil(Func<bool> condition, int timeoutMs)
{
    var watch = Stopwatch.StartNew();
    while (watch.ElapsedMilliseconds < timeoutMs)
    {
        if (condition())
        {
            return true;
        }

        Thread.Sleep(20);
    }

    return condition();
}

static bool NotDescending(IEnumerable<TickMessage> messages)
{
    var previous = int.MinValue;
    foreach (var message in messages)
    {
        if (message.Sequence < previous)
        {
            return false;
        }

        previous = message.Sequence;
    }

    return true;
}

// 后端子进程：连之前先发三条（状态类写黑板、通知类不写、进程内的不出进程），之后每 200 毫秒推一条状态类、一条通知类
static async Task RunServer(int port)
{
    RemoteEventPublisher.Send(new TickMessage { Sequence = 0, Token = "tick" }, "tick");
    RemoteEventPublisher.Send(new TickMessage { Sequence = -100, Token = "notice" }, "notice", retain: false);
    EventBus.Send(new TickMessage { Sequence = -1, Token = "tick" }, "tick");

    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(options =>
        options.ListenLocalhost(port, listen => listen.Protocols = HttpProtocols.Http2)); //设置http2
    builder.Services.AddCodeFirstGrpc(); //CodeFirst 模式
    var app = builder.Build();
    app.MapGrpcService<EventService>(); //

    _ = Task.Run(async () =>
    {
        var sequence = 0;
        while (true)
        {
            await Task.Delay(200);
            sequence++;
            RemoteEventPublisher.Send(new TickMessage { Sequence = sequence, Token = "live" }, "live");
            RemoteEventPublisher.Send(new TickMessage { Sequence = sequence, Token = "notice" }, "notice", retain: false);
        }
    });

    await app.RunAsync();
}

/// <summary>
/// 跨进程用的消息：带上自己的 token，收的一头能查有没有串
/// </summary>
public class TickMessage
{
    public int Sequence { get; set; }

    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// 进程内用的消息
/// </summary>
public class LocalMessage
{
    public string Text { get; set; } = string.Empty;
}
