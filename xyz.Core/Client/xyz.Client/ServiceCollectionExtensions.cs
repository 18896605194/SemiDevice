using Microsoft.Extensions.DependencyInjection;
using xyz.Client.Alarm;
using xyz.Client.DataCenter;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Io;
using xyz.Client.Menus;
using xyz.Client.Modules;
using xyz.Client.Setting;
using xyz.Client.ViewModels;
using xyz.Client.Views;

namespace xyz.Client;

/// <summary>
/// 客户端服务注册扩展。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXyzClientServices(this IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<MainViewModel>());
        services.AddSingleton<TopBarViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<TopBarViewModel>());
        services.AddTransient<PlaceholderView>();

        // 平台菜单写在代码里；机型菜单由 ClientModuleLoader 按模块声明补进来。
        services.AddSingleton<IClientMenuProvider, PlatformMenuProvider>();

        services.AddXyzSettingServices();
        services.AddXyzIoServices();
        services.AddXyzDataCenterServices();
        services.AddXyzAlarmServices();

        return services;
    }
}
