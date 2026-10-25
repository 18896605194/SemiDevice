using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ProtoBuf.Grpc.Server;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Logging;
using xyz.Service;
using xyz.Tools;

namespace xyz.GrpcHost;

[SupportedOSPlatform("windows")]
public static class Program
{
    public static void Main(string[] args)
    {
        // 一台机只跑一个后端（灯绿灯红都算在跑）：再双击 exe 只让已有那颗灯冒气泡提示，这个直接退，不再去连一遍设备。
        using var instance = HostTrayIcon.ClaimInstance();
        if (instance is null)
        {
            return;
        }

        // 没有控制台了，崩溃原因只能进日志：日志是异步写的，记完等它写进文件再让进程没掉。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogHelper.Error(LogHelper.HostModule, $"后端异常退出：{e.ExceptionObject}");
            LogHelper.Flush(TimeSpan.FromSeconds(3));
        };

        // 右下角状态灯：灰 = 启动中，绿 = 运行中，红 = 启动失败。
        using var tray = new HostTrayIcon();
        try
        {
            Run(args, tray);
            LogHelper.Info(LogHelper.HostModule, "后端已退出");
        }
        catch (Exception exception) when (tray.ExitRequested.IsCancellationRequested)
        {
            LogHelper.Error(LogHelper.HostModule, $"后端退出时出错：{exception}");
        }
        catch (Exception exception)
        {
            LogHelper.Error(LogHelper.HostModule, $"后端启动失败：{exception}");
            tray.SetFailed(exception.Message);
            tray.WaitForExit(); // 红灯留着，等人在托盘上点退出
        }
        finally
        {
            // 日志队列里还没写的写完、文件关掉
            LogHelper.Shutdown();
        }
    }

    private static void Run(string[] args, HostTrayIcon tray)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 宿主（ASP.NET Core、gRPC 框架）自己的日志也走 LogHelper，记在 Host 名下，只转 Warn 以上
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new LogHelperLoggerProvider(LogHelper.HostModule));

        // 照 machine.xml 装组件树，组件和 gRPC 服务进容器；组件等容器建好以后再开机
        builder.Services.AddXyzServices();

        // 只走 HTTP/2；监听哪个地址、端口等组件起来以后从 Rpc 组件上取（见下面 app.Urls）
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ConfigureEndpointDefaults(listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        // 退出时不干等长连接：客户端的事件流不会自己断，按默认 30 秒超时要等半分钟才退得掉。
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(3));

        // 单条消息上限从 Rpc 组件取：这个选项第一次用到是在映射服务的时候（已经开完机），那时容器里取得到。
        // 所有调用都过 ActionFilter：记操作日志（谁点了什么）
        builder.Services.AddCodeFirstGrpc(options =>
        {
            options.MaxReceiveMessageSize = MaxMessageSizeMb() * 1024 * 1024;
            options.Interceptors.Add<ActionFilter>();
        });

        var app = builder.Build();

        // 容器建好了再开机：组件按阶段起来（PLC 先连先扫描），都起来以后才开 gRPC 端口；退出时各组件按开机的倒序收尾
        app.Services.StartComponents();
        app.Lifetime.ApplicationStopping.Register(ServiceExtensions.StopComponents);

        // gRPC 端点本身是组件（machine.xml 的 Rpc 节点），地址端口从它上面取；没配该节点退回默认 localhost:5000
        var rpc = IocHelper.GetService<IRpc>();
        string host = "localhost";
        if (rpc is not null && !string.IsNullOrEmpty(rpc.Host))
        {
            host = rpc.Host;
        }

        int port = rpc?.Port ?? RpcComponent.DefaultPort;
        app.Urls.Add(ListenUrl(host, port));

        // gRPC 服务：实现了契约的类都映射上（加服务不用改这里）
        foreach (var implementation in ServiceExtensions.GrpcServices().Select(service => service.Implementation).Distinct())
        {
            MapGrpcService(app, implementation);
        }

        // 端口监听上了灯才变绿；托盘上点退出就停宿主。
        app.Lifetime.ApplicationStarted.Register(() => tray.SetRunning($"{host}:{port}"));
        tray.ExitRequested.Register(app.Lifetime.StopApplication);

        app.Run();
    }

    /// <summary>
    /// 单条消息最大多少 MB：没配 Rpc 节点按默认 26 MB
    /// </summary>
    private static int MaxMessageSizeMb()
    {
        var rpc = IocHelper.GetService<IRpc>();
        return rpc?.MaxMessageSizeMb ?? RpcComponent.DefaultMaxMessageSizeMb;
    }

    /// <summary>
    /// 监听地址：localhost 监听本机的两个回环（IPv4、IPv6）；别的按明确地址监听（远程调试，IPv6 加方括号）
    /// </summary>
    private static string ListenUrl(string host, int port)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return $"http://localhost:{port}";
        }

        var address = IPAddress.Parse(host);
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return $"http://[{address}]:{port}";
        }

        return $"http://{address}:{port}";
    }

    /// <summary>
    /// 按类型映射一个 gRPC 服务（按类型映射的只有泛型版本，另两个非泛型重载收的是服务定义）
    /// </summary>
    private static void MapGrpcService(WebApplication app, Type implementation)
    {
        var map = typeof(GrpcEndpointRouteBuilderExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService) && method.IsGenericMethodDefinition);
        map.MakeGenericMethod(implementation).Invoke(null, [app]);
    }
}
