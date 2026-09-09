using System.Diagnostics;
using NLog;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.Presentation.ViewModels;
using xyz.Common.Log;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

// 跨进程模式：连到正在运行的后端，用真实客户端的组合（RemoteEventBus → EventBus → ClientLog → LogViewModel）
// 数一段时间内界面能看到的日志条数。
//   dotnet run --project tools/LogPipelineSmoke -- client [秒数]
if (args.Length > 0 && args[0] == "client")
{
    var seconds = args.Length > 1 ? int.Parse(args[1]) : 8;

    GrpcClientFactory.Initialize();
    RemoteEventBus.Initialize();

    using var clientViewModel = new LogViewModel();
    clientViewModel.Init();

    await Task.Delay(TimeSpan.FromSeconds(seconds));

    var service = GrpcClientFactory.Create<ILogService>();
    var response = await service.GetRecentAsync(new LogQuery { Count = 10 });
    var data = response.Data.Length > 160 ? response.Data[..160] : response.Data;
    Console.WriteLine($"client mode: connected={RemoteEventBus.IsConnected} viewModel.Logs={clientViewModel.Logs.Count} " +
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
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

var delivered = new List<LogItem>();
var gate = new object();
int DeliveredCount() { lock (gate) return delivered.Count; }
bool WaitFor(int target, int timeoutMs = 5000)
{
    var watch = Stopwatch.StartNew();
    while (watch.ElapsedMilliseconds < timeoutMs)
    {
        if (DeliveredCount() >= target) return true;
        Thread.Sleep(10);
    }
    return false;
}

LogQueue.MinLevel = LogLevel.Debug;
LogQueue.Start(item => { lock (gate) delivered.Add(item); });

// 1) 单生产者：顺序与不丢
for (var i = 0; i < 1000; i++)
{
    LogQueue.Enqueue(new LogItem("Order", LogLevel.Warn, i.ToString()));
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
        LogQueue.Enqueue(new LogItem("Concurrent", LogLevel.Warn, $"{worker}-{i}"));
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
LogQueue.Enqueue(new LogItem("Filter", LogLevel.Debug, "debug-should-drop"));
LogQueue.Enqueue(new LogItem("Filter", LogLevel.Info, "info-should-drop"));
LogQueue.Enqueue(new LogItem("Filter", LogLevel.Warn, "warn-should-pass"));
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
    Check(delivered[^1].Module == "SmokeModule" && delivered[^1].Message == "hello from LogHelper",
        "LogHelper 入队内容被改变。");
}

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

// 6) 跨进程序列化：LogDto 经信封 JSON 往返
var original = new LogDto
{
    Time = DateTime.Now,
    Level = "Error",
    Module = "LoadPort1",
    Message = "中文消息 {0}",
    Source = "Server",
};
var envelope = EventEnvelope.Of(original, LogDto.EventToken, retain: false);
Check(envelope.TypeName == typeof(LogDto).FullName, "信封类型名被改变。");
var roundTrip = (LogDto)EventEnvelope.From(envelope, typeof(LogDto));
Check(roundTrip.Level == original.Level
      && roundTrip.Module == original.Module
      && roundTrip.Message == original.Message
      && roundTrip.Source == original.Source,
    "LogDto 往返后内容被改变。");

// 7) Stop 后不再投递
LogQueue.Stop();
var afterStop = DeliveredCount();
LogQueue.Enqueue(new LogItem("AfterStop", LogLevel.Error, "should-drop"));
Thread.Sleep(200);
Check(DeliveredCount() == afterStop, "Stop 后仍在投递。");

Console.WriteLine($"PASS: {checks} log pipeline checks (queue order, concurrency, level filter, LogHelper, LogViewModel, JSON round-trip, stop).");
return 0;
