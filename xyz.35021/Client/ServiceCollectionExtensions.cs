using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.Manual.Views;

namespace xyz._35021.Client;

/// <summary>
/// 35021 机型模块的客户端服务注册：页面按菜单 Code 注册成 keyed UserControl，
/// 平台主界面按菜单 Code 从 DI 取页面塞进中间内容区。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXyz35021ClientServices(this IServiceCollection services)
    {
        // ModuleName 必须与 sc.xml 的模块实例名一致（LoadPort1 / LoadPort2）。
        services.AddKeyedSingleton<UserControl>("Manual.LoadPort1",(_, _) => new LoadPortManualControl { ModuleName = "LoadPort1" });
        services.AddKeyedSingleton<UserControl>("Manual.LoadPort2",(_, _) => new LoadPortManualControl { ModuleName = "LoadPort2" });

        return services;
    }
}
