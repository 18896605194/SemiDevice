using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.DataModels.ViewModels;
using xyz._35021.Client.Manual.ViewModels;
using xyz._35021.Client.Manual.Views;

namespace xyz._35021.Client;

/// <summary>
/// 35021 机型模块的客户端服务注册：页面按菜单 Code 注册成 keyed UserControl，
/// 平台主界面按菜单 Code 从 DI 取页面塞进中间内容区。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXyz35021ClientServices(this IServiceCollection services)
    {
        // 大手动界面：一个页面里按 sc.xml 配的 LoadPort 数量动态生成多个面板。
        services.AddSingleton<LoadPortsManualViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<LoadPortsManualViewModel>());

        services.AddKeyedSingleton<UserControl, LoadPortsManualView>("Manual.LoadPorts");

        return services;
    }
}
