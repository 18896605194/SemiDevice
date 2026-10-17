using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ProtoBuf.Grpc.Server;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Service;

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

        // 没有控制台了，崩溃原因只能进日志。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogHelper.Error("Host", $"后端异常退出：{e.ExceptionObject}");

        // 右下角状态灯：灰 = 启动中，绿 = 运行中，红 = 启动失败。
        using var tray = new HostTrayIcon();
        try
        {
            Run(args, tray);
            LogHelper.Info("Host", "后端已退出");
        }
        catch (Exception exception) when (tray.ExitRequested.IsCancellationRequested)
        {
            LogHelper.Error("Host", $"后端退出时出错：{exception}");
        }
        catch (Exception exception)
        {
            LogHelper.Error("Host", $"后端启动失败：{exception}");
            tray.SetFailed(exception.Message);
            tray.WaitForExit(); // 红灯留着，等人在托盘上点退出
        }
    }

    private static void Run(string[] args, HostTrayIcon tray)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 先装配组件树再配监听：gRPC 端点本身是组件（sc.xml 的 Rpc 节点），地址端口从 RpcComponent 上取；
        // 没配该节点时退回默认 localhost:5000，行为与老版本一致。组件照旧先于端口装配（PLC 先连先扫描的启动顺序不变）。
        builder.Services.AddXyzServices();
        var rpc = ComponentBase.Find<IRpcComponent>();
        var host = rpc is not null && !string.IsNullOrEmpty(rpc.Host) ? rpc.Host : "localhost";
        var port = rpc?.Port ?? RpcComponent.DefaultPort;

        builder.WebHost.ConfigureKestrel(options =>
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                options.ListenLocalhost(port, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            }
            else
            {
                // 远程调试场景：按明确地址监听（IPv4/IPv6 均可）
                options.Listen(IPAddress.Parse(host), port, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            }
        });

        // 退出时不干等长连接：客户端的事件流不会自己断，按默认 30 秒超时要等半分钟才退得掉。
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(3));

        builder.Services.AddCodeFirstGrpc();

        var app = builder.Build();

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            // 各组件自己收尾（EAP 断链路、数据曲线落库、断 PLC、晶圆账存盘），按开机的倒序
            ComponentLoader.StopAll(ComponentBase.Roots);
        });

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
    /// 按类型映射一个 gRPC 服务（按类型映射的只有泛型版本，另两个非泛型重载收的是服务定义）
    /// </summary>
    private static void MapGrpcService(WebApplication app, Type implementation)
    {
        var map = typeof(GrpcEndpointRouteBuilderExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(GrpcEndpointRouteBuilderExtensions.MapGrpcService) && method.IsGenericMethodDefinition);
        map.MakeGenericMethod(implementation).Invoke(null, [app]);
    }
}
