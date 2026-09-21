using System.Runtime.Versioning;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ProtoBuf.Grpc.Server;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Components;
using xyz.Service;
using xyz.Service.Alarms;
using xyz.Service.Events;
using xyz.Service.Systems;
using xyz.Service.UserManger;

namespace xyz.GrpcHost;

[SupportedOSPlatform("windows")]
public static class Program
{
    private const int Port = 5000;

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

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        // 退出时不干等长连接：客户端的事件流不会自己断，按默认 30 秒超时要等半分钟才退得掉。
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(3));

        builder.Services.AddCodeFirstGrpc();

        builder.Services.AddXyzServices();

        var app = builder.Build();

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            var roots = app.Services.GetRequiredService<IReadOnlyList<ComponentBase>>();
            foreach (var axis in roots.OfType<AxisComponent>()
                         .Concat(roots.SelectMany(root => root.FindChildren<AxisComponent>())).Distinct())
                axis.Close();
            foreach (var io in roots.OfType<IoComponent>())
            {
                io.StopCollecting();
            }
            foreach (var plc in roots.OfType<PlcComponent>()) plc.Close();
        });

        app.MapGrpcService<RpcService>();
        app.MapGrpcService<RoleService>();
        app.MapGrpcService<UserService>();
        app.MapGrpcService<EventService>();
        app.MapGrpcService<LoadPortService>();
        app.MapGrpcService<RobotService>();
        app.MapGrpcService<LogService>();
        app.MapGrpcService<AlarmService>();
        app.MapGrpcService<SystemService>();

        // 端口监听上了灯才变绿；托盘上点退出就停宿主。
        app.Lifetime.ApplicationStarted.Register(() => tray.SetRunning($"localhost:{Port}"));
        tray.ExitRequested.Register(app.Lifetime.StopApplication);

        app.Run();
    }
}
