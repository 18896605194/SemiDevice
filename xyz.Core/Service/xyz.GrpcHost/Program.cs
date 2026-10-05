using System.Net;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ProtoBuf.Grpc.Server;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Components;
using xyz.Service;
using xyz.Service.Alarms;
using xyz.Service.Charts;
using xyz.Service.Events;
using xyz.Service.Jobs;
using xyz.Service.Recipes;
using xyz.Service.Systems;
using xyz.Service.Transfers;
using xyz.Service.UserManger;
using xyz.Service.Wafers;

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
        var rpc = RpcComponent.Current;
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
            // EAP 链路先优雅断开（发 Separate），再停采样、落库、断 PLC。
            HsmsComponent.Current?.Close();

            // 先停采样、把攒着的最后一批写进库，再断 PLC。
            DataChartComponent.Current?.StopSampling();

            // 晶圆账最后存一次盘：下次开机按它把腔体、机械手上的片放回去。
            WaferManager.Current?.StopSnapshot();

            var roots = app.Services.GetRequiredService<IReadOnlyList<ComponentBase>>();
            foreach (var plc in roots.OfType<PlcComponent>())
            {
                plc.Close();
            }
        });

        app.MapGrpcService<RpcService>();
        app.MapGrpcService<RoleService>();
        app.MapGrpcService<UserService>();
        app.MapGrpcService<EventService>();
        app.MapGrpcService<LoadPortService>();
        app.MapGrpcService<RobotService>();
        app.MapGrpcService<ChamberService>();
        app.MapGrpcService<LogService>();
        app.MapGrpcService<AlarmService>();
        app.MapGrpcService<SystemService>();
        app.MapGrpcService<EquipmentService>();
        app.MapGrpcService<IoService>();
        app.MapGrpcService<EcService>();
        app.MapGrpcService<WaferLedgerService>();
        app.MapGrpcService<SequenceService>();
        app.MapGrpcService<ProcessRecipeService>();
        app.MapGrpcService<DataChartService>();
        app.MapGrpcService<RealChartService>();
        app.MapGrpcService<TransferService>();
        app.MapGrpcService<JobService>();

        // 端口监听上了灯才变绿；托盘上点退出就停宿主。
        app.Lifetime.ApplicationStarted.Register(() => tray.SetRunning($"{host}:{port}"));
        tray.ExitRequested.Register(app.Lifetime.StopApplication);

        app.Run();
    }
}
