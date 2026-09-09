using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ProtoBuf.Grpc.Server;
using xyz.Client.Common.Events;
using xyz.Client.Common.Rpc;
using xyz.Service.Events;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace EventBusSmoke;

/// <summary>
/// 事件通道冒烟测试：
///   dotnet run -- server   启动事件后端（:5001，每秒发两条消息）
///   dotnet run -- client   启动客户端订阅 + 上行
/// 验证点：
///   1. 进程内 Send/Register
///   2. 跨进程下发（服务端流）
///   3. 留存重放（客户端晚连上，先收到历史最后一条）
///   4. token 区分（tick/tock 互不串扰）
///   5. Register 晚于收包也能补发留存
///   6. 客户端上行
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Contains("server"))
        {
            await RunServer();
        }
        else if (args.Contains("probe"))
        {
            await RunProbe();
        }
        else
        {
            await RunClient();
        }
    }

    /// <summary>
    /// 诊断模式：不走 RemoteEventBus 的静默 catch，直接调用并打印异常。
    /// </summary>
    private static async Task RunProbe()
    {
        GrpcClientFactory.Initialize("http://localhost:5001");
        try
        {
            var service = GrpcClientFactory.Create<IEventService>();
            var response = await service.PublishAsync(EventEnvelope.Of(new ShoutMessage { Text = "probe" }));
            Console.WriteLine($"[probe] PublishAsync 成功: Success={response.Success}");

            var stream = service.SubscribeAsync(new EventSubscription());
            Console.WriteLine("[probe] SubscribeAsync 成功，读取第一条...");
            await foreach (var message in stream)
            {
                Console.WriteLine($"[probe] 收到 TypeName={message.TypeName} Token={message.Token} Payload={message.Payload}");
                break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[probe] 失败: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex);
        }
    }

    private static async Task RunServer()
    {
        Console.WriteLine($"[server] pid={Environment.ProcessId} 监听 http://localhost:5001");

        // 验证点 1：进程内订阅
        EventBus.Register<TickMessage>("tick", m =>
            Console.WriteLine($"[server][进程内] tick #{m.Sequence}"));
        EventBus.Register<ShoutMessage>(m =>
            Console.WriteLine($"[server][上行收到] {m.Text}"));
        EventBus.DeadLetter += (m, ex) =>
            Console.WriteLine($"[server][死信] {m.TypeName ?? "<null>"}/{m.Token ?? "<null>"}: {ex.Message}");

        // 自测：不经过 gRPC，直接 Deliver 一个信封，验证派发路径
        EventBus.Deliver(EventEnvelope.Of(new ShoutMessage { Text = "self-test" }));
        Console.WriteLine("[server] 自测 Deliver 已执行（上方应打印 上行收到 self-test）");

        // 验证点 3：客户端连上之前先发一条留存消息（#0）
        EventBus.Send(new TickMessage { Sequence = 0, Time = DateTime.Now }, "tick");
        Console.WriteLine("[server] 已预发留存消息 tick #0（等客户端连上后应立即收到）");

        // 周期发送：tick 每 1s，tock 每 3s
        _ = Task.Run(async () =>
        {
            var sequence = 0;
            while (true)
            {
                await Task.Delay(1000);
                sequence++;
                EventBus.Send(new TickMessage { Sequence = sequence, Time = DateTime.Now }, "tick");
                if (sequence % 3 == 0)
                {
                    EventBus.Send(new TickMessage { Sequence = sequence, Time = DateTime.Now }, "tock");
                    Console.WriteLine($"[server] 已发送 tick/tock #{sequence}");
                }
            }
        });

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.ListenLocalhost(5001, listenOptions => listenOptions.Protocols = HttpProtocols.Http2));
        builder.Services.AddCodeFirstGrpc();
        var app = builder.Build();
        app.MapGrpcService<EventService>();
        await app.RunAsync();
    }

    private static async Task RunClient()
    {
        Console.WriteLine($"[client] pid={Environment.ProcessId} 连接 http://localhost:5001");

        // 先订阅再启动流泵，避免竞态
        EventBus.Register<TickMessage>("tick", m =>
            Console.WriteLine($"[client][跨进程][tick] #{m.Sequence} @ {m.Time:HH:mm:ss.fff}"));
        EventBus.Register<TickMessage>("tock", m =>
            Console.WriteLine($"[client][跨进程][tock] #{m.Sequence} @ {m.Time:HH:mm:ss.fff}"));

        GrpcClientFactory.Initialize("http://localhost:5001");
        RemoteEventBus.Initialize();
        RemoteEventBus.ConnectionChanged += ok =>
            Console.WriteLine(ok ? "[client] === 事件流已连上 ===" : "[client] === 事件流断开 ===");

        await Task.Delay(3000);

        // 验证点 5：晚注册也能补发留存（新 token，服务端从未发过 → 无补发；tick 已有留存）
        EventBus.Register<TickMessage>("late", m =>
            Console.WriteLine($"[client][晚注册补发][late] #{m.Sequence}（服务端没发过 late，不应出现）"));
        EventBus.Register<TickMessage>("tick", m =>
            Console.WriteLine($"[client][晚注册补发][tick] #{m.Sequence}（应立即出现一次当前值）"));

        Console.WriteLine("[client] 上行发送 ShoutMessage ...");
        RemoteEventBus.SendToServer(new ShoutMessage { Text = "hello from client" });

        await Task.Delay(4000);
        Console.WriteLine("[client] 冒烟结束");
    }
}

public class TickMessage
{
    public int Sequence { get; set; }

    public DateTime Time { get; set; }
}

public class ShoutMessage
{
    public string Text { get; set; } = string.Empty;
}
