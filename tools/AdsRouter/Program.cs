using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwinCAT.Ads;
using TwinCAT.Ads.TcpRouter;

// 开发机用的 ADS 路由：没装 TwinCAT 时顶替 TwinCAT 的路由服务，仿真器（ADS 服务端）和后端（AdsClient）都经它通信。
// 装成 Windows 服务开机自启（install-service.ps1），也能直接双击在控制台里跑。真机上 TwinCAT 自带路由，不用它。
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = RouterService.ServiceName);
builder.Services.AddHostedService<RouterService>();
builder.Build().Run();

internal sealed class RouterService(ILogger<RouterService> logger, ILoggerFactory loggerFactory, IConfiguration configuration)
    : BackgroundService
{
    public const string ServiceName = "xyzAdsRouter";

    /// <summary>ADS 路由的固定端口；TwinCAT 的路由也占它，两个不能同时跑。</summary>
    private const int RouterPort = 48898;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (IsPortInUse(RouterPort))
        {
            logger.LogError("端口 {Port} 已被占用：本机已有 ADS 路由（装了 TwinCAT 或本程序已在运行），不再启动", RouterPort);
            return;
        }

        var netId = ResolveNetId();
        var router = new AmsTcpIpRouter(netId, loggerFactory);
        router.RouterStatusChanged += (_, e) => logger.LogInformation("路由状态：{Status}", e.RouterStatus);

        logger.LogInformation("ADS 路由启动：本机 AmsNetId = {NetId}，端口 {Port}", netId, RouterPort);
        try
        {
            await router.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            router.Stop();
            logger.LogInformation("ADS 路由已停止");
        }
    }

    /// <summary>
    /// 本机 AmsNetId：配置里给了（appsettings.json 的 NetId）就用它，否则按本机 IPv4 + ".1.1"，跟 TwinCAT 默认的取法一样。
    /// 仿真器和后端都按"本机"连（后端 sc.xml 的 Host = Local），所以这个值变了也不用改它们的配置。
    /// </summary>
    private AmsNetId ResolveNetId()
    {
        var configured = configuration["NetId"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return AmsNetId.Parse(configured);
        }

        var address = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                          && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            ?? IPAddress.Loopback;

        return AmsNetId.Parse($"{address}.1.1");
    }

    private static bool IsPortInUse(int port)
    {
        return IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Any(endpoint => endpoint.Port == port);
    }
}
