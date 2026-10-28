using System.Diagnostics;
using NLog;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.Presentation.Models;
using xyz.Client.Presentation.ViewModels;
using xyz.Common.Events;
using xyz.Common.Log;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

// 跨进程模式：连到正在运行的后端，用真实客户端的组合（RemoteEventConnection → RemoteEventSubscriber → ClientLog → LogViewModel）
// 数一段时间内界面能看到的日志条数。
//   dotnet run --project tools/LogPipelineSmoke -- client [秒数]
if (args.Length > 0 && args[0] == "client")
{
    var seconds = args.Length > 1 ? int.Parse(args[1]) : 8;

    GrpcClientFactory.Initialize();
    RemoteEventConnection.Initialize();

    using var clientViewModel = new LogViewModel();
    clientViewModel.Init();

    await Task.Delay(TimeSpan.FromSeconds(seconds));

    var service = GrpcClientFactory.Create<ILogService>();
    var response = await service.GetRecentAsync(RpcRequest.Of((RpcParams.Count, 10)));
    var data = response.Data.Length > 160 ? response.Data[..160] : response.Data;
    Console.WriteLine($"client mode: connected={RemoteEventConnection.IsConnected} viewModel.Logs={clientViewModel.Logs.Count} " +
                      $"GetRecent.Success={response.Success} Data={data}");
    if (clientViewModel.Logs.Count > 0)
    {
        var last = clientViewModel.Logs[^1];
        Console.WriteLine($"sample: 【{last.Level}】 {last.SourceText} {last.Module} {last.Message}");
    }

    return clientViewModel.Logs.Count > 0 ? 0 : 2;
}

// 进程内模式：队列语义 / LogHelper 入队 / 客户端消费 / 跨进程序列化。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    checks++;
}

var delivered = new List<LogItem>();
var gate = new object();
int DeliveredCount()
{
    lock (gate)
    {
        return delivered.Count;
    }
}

bool WaitFor(int target, int timeoutMs = 5000)
{
    var watch = Stopwatch.StartNew();
    while (watch.ElapsedMilliseconds < timeoutMs)
    {
        if (DeliveredCount() >= target)
        {
            return true;
        }

        Thread.Sleep(10);
    }
    return false;
}

LogQueue.MinLevel = LogLevel.Debug;
LogQueue.Start(item =>
{
    lock (gate)
    {
        delivered.Add(item);
    }
});

// 1) 单生产者：顺序与不丢
for (var i = 0; i < 1000; i++)
{
    LogQueue.Enqueue(new LogItem("Order", string.Empty, LogLevel.Warn, i.ToString()));
}
Check(WaitFor(1000), "队列未投递 1000 条。");
lock (gate)
{
    var actual = delivered.Take(1000).Select(item => item.Message);
    Check(actual.SequenceEqual(Enumerable.Range(0, 1000).Select(i => i.ToString())),
        "队列投递顺序被改变。");
}

// 2) 多生产者并发：不丢不重
var baseline = DeliveredCount();
const int concurrentTotal = 1000;
await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
{
    for (var i = 0; i < concurrentTotal / 4; i++)
    {
        LogQueue.Enqueue(new LogItem("Concurrent", string.Empty, LogLevel.Warn, $"{worker}-{i}"));
    }
})));
Check(WaitFor(baseline + concurrentTotal), "并发入队丢消息。");
lock (gate)
{
    var unique = delivered.Skip(baseline).Select(item => item.Message).ToHashSet();
    Check(unique.Count == concurrentTotal, "并发入队出现重复或缺失。");
}

// 3) 级别过滤：低于 MinLevel 只落 NLog，不进队列
var beforeFilter = DeliveredCount();
LogQueue.MinLevel = LogLevel.Warn;
LogQueue.Enqueue(new LogItem("Filter", string.Empty, LogLevel.Debug, "debug-should-drop"));
LogQueue.Enqueue(new LogItem("Filter", string.Empty, LogLevel.Info, "info-should-drop"));
LogQueue.Enqueue(new LogItem("Filter", string.Empty, LogLevel.Warn, "warn-should-pass"));
Check(WaitFor(beforeFilter + 1), "Warn 未投递。");
Thread.Sleep(200);
Check(DeliveredCount() == beforeFilter + 1, "级别过滤漏了低级别日志。");
lock (gate)
{
    Check(delivered[^1].Message == "warn-should-pass", "通过过滤的不是 Warn。");
}
LogQueue.MinLevel = LogLevel.Debug;

// 4) LogHelper 入队（后端统一入口）
var beforeHelper = DeliveredCount();
LogHelper.Warn("SmokeModule", "hello from LogHelper");
Check(WaitFor(beforeHelper + 1), "LogHelper 未入队。");
lock (gate)
{
    Check(delivered[^1].Module == "SmokeModule" && delivered[^1].Part.Length == 0 && delivered[^1].Message == "hello from LogHelper",
        "LogHelper 入队内容被改变。");
}

// 4.1) 元件写的带两层名字：第一层模块、第二层部件
var beforeSource = DeliveredCount();
LogHelper.Warn(new SmokeSource("LoadPort.LoadPort1", "RFID.Serial"), "读码超时");
Check(WaitFor(beforeSource + 1), "带两层名字的日志没入队。");
lock (gate)
{
    Check(delivered[^1].Module == "LoadPort.LoadPort1" && delivered[^1].Part == "RFID.Serial",
        "两层名字：第一层 LoadPort.LoadPort1、第二层 RFID.Serial");
    Check(delivered[^1].ToString().Contains("【WARN】 【LoadPort.LoadPort1】 【RFID.Serial】 读码超时"),
        "写进文件的一行：级别、模块、部件各一格");
}

// 4.2) 操作日志、收发原文、轮询原文只落文件，不进推送队列（不推顶栏）
var beforeFileOnly = DeliveredCount();
LogHelper.Operation(LogLevel.Warn, "张三@127.0.0.1 LoadPort.Load → 失败 module.action_rejected [LoadPort1, 3] 1 ms");
LogHelper.Comm(new SmokeSource("LoadPort.LoadPort1", "Serial"), LogHelper.Sent, "MOV:CLOAD");
LogHelper.InfoLoop(new SmokeSource("LoadPort.LoadPort1", "Serial"), LogHelper.Received, "ACK:STATE/0100");
Thread.Sleep(200);
Check(DeliveredCount() == beforeFileOnly, "操作日志、收发原文、轮询原文都不进推送队列");

// 4.3) 收发原文里看不见的字符写成 <CR> 这种，能显示的（包括中文）原样
Check(LogHelper.Escape("s00MOV:CLOAD;\r\n\u0002中文") == "s00MOV:CLOAD;<CR><LF><0x02>中文",
    "原文转义：回车换行和控制字符写成尖括号，中文原样");

// 5) 客户端：队列 → LogViewModel（界面即消费者）
var viewModel = new LogViewModel();
viewModel.Init();
ClientLog.Error("LoadPort1", "Load 失败：module.action_rejected");
var vmWatch = Stopwatch.StartNew();
while (viewModel.Logs.Count == 0 && vmWatch.ElapsedMilliseconds < 5000)
{
    Thread.Sleep(10);
}
Check(viewModel.Logs.Count > 0, "LogViewModel 未从队列取到日志。");
Check(viewModel.Logs[^1].Level == "Error"
      && viewModel.Logs[^1].Message.Contains("action_rejected")
      && viewModel.Logs[^1].SourceText == "[C]",
    "LogModel 映射被改变。");
viewModel.Dispose();

// 6) 跨进程序列化：LogDto 经 RemoteEventPublisher 打包成信封，再由 RemoteEventSubscriber 拆回来
var original = new LogDto
{
    Time = DateTime.Now,
    Level = "Error",
    Module = "LoadPort.LoadPort1",
    Part = "Serial",
    Message = "中文消息 {0}",
    Source = "Server",
};
var sent = new List<EventMessage>();
using (RemoteEventPublisher.SubscribeSent(sent.Add))
{
    RemoteEventPublisher.Send(original, LogDto.EventToken, retain: false);
}
Check(sent.Count == 1 && sent[0].TypeName == typeof(LogDto).FullName, "信封类型名被改变。");
var received = new List<LogDto>();
using (RemoteEventSubscriber.Register<LogDto>(LogDto.EventToken, received.Add))
{
    RemoteEventSubscriber.Deliver(sent[0]);
}
Check(received.Count == 1
      && received[0].Level == original.Level
      && received[0].Module == original.Module
      && received[0].Part == original.Part
      && received[0].Message == original.Message
      && received[0].Source == original.Source,
    "LogDto 往返后内容被改变。");
Check(LogModel.From(received[0]).Part == "Serial", "界面的日志模型带上部件");

// 7) Stop 后不再投递
LogQueue.Stop();
var afterStop = DeliveredCount();
LogQueue.Enqueue(new LogItem("AfterStop", string.Empty, LogLevel.Error, "should-drop"));
Thread.Sleep(200);
Check(DeliveredCount() == afterStop, "Stop 后仍在投递。");

Console.WriteLine($"PASS: {checks} log pipeline checks (queue order, concurrency, level filter, LogHelper, two-level names, file-only lines, escape, LogViewModel, JSON round-trip, stop).");
return 0;

/// <summary>
/// 冒烟里的日志来源：两层名字由测试给
/// </summary>
sealed class SmokeSource(string module, string part) : ILogSource
{
    public string LogModule => module;

    public string LogPart => part;
}
