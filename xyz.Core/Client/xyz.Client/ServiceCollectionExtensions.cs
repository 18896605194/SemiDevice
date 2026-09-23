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
    /// <summary>
    /// 注册客户端服务。modules 是后端 sc.xml 里装了的模块名，按模块分的页面与菜单（IO）照它生成——
    /// sc.xml 里没配的模块，菜单里就不该出现。后端没起时是空的，那些菜单一并不出现。
    /// </summary>
    public static IServiceCollection AddXyzClientServices(
        this IServiceCollection services, IReadOnlyList<string> modules)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<MainViewModel>());
        services.AddSingleton<TopBarViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<TopBarViewModel>());
        services.AddTransient<PlaceholderView>();

        // 平台菜单写在代码里；IO 下按模块分的二级菜单照后端装的模块生成。
        services.AddSingleton<IClientMenuProvider>(new PlatformMenuProvider(modules));

        services.AddXyzSettingServices();

        // IO 页面一个模块一页，跟上面的二级菜单一一对应。
        foreach (var module in modules)
        {
            services.AddXyzIoModule(module);
        }

        services.AddXyzDataCenterServices();
        services.AddXyzAlarmServices();

        return services;
    }
}
