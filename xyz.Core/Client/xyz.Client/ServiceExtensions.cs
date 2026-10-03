using Microsoft.Extensions.DependencyInjection;
using xyz.Client.Alarm;
using xyz.Client.DataCenter;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Io;
using xyz.Client.Manual;
using xyz.Client.Menus;
using xyz.Client.Modules;
using xyz.Client.Recipe;
using xyz.Client.Setting;
using xyz.Client.ViewModels;
using xyz.Client.Views;
using xyz.Shared.Dtos;

namespace xyz.Client;

/// <summary>
/// 客户端服务注册扩展。
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// 注册客户端服务。settings 里是后端 sc.xml 里装了的模块名（及其中的腔体），按模块分的页面与菜单（IO、腔体手动）照它生成——
    /// sc.xml 里没配的模块，菜单里就不该出现。后端没起时是空的，那些菜单一并不出现。
    /// </summary>
    public static IServiceCollection AddXyzClientServices(
        this IServiceCollection services, SystemSettingsDto settings)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<MainViewModel>());
        services.AddSingleton<TopBarViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<TopBarViewModel>());
        services.AddTransient<PlaceholderView>();

        // 平台菜单写在代码里；IO 下、Manual 下按模块分的二级菜单照后端装的模块生成。
        services.AddSingleton<IClientMenuProvider>(new PlatformMenuProvider(settings.Modules, settings.Chambers));

        services.AddXyzSettingServices();
        services.AddXyzRecipeServices();

        // IO 页面一个模块一页，跟上面的二级菜单一一对应。
        foreach (var module in settings.Modules)
        {
            services.AddXyzIoModule(module);
        }

        // 腔体手动页一个腔体一页，跟 Manual 下的腔体子菜单一一对应。
        foreach (var chamber in settings.Chambers)
        {
            services.AddXyzChamberManual(chamber);
        }

        services.AddXyzDataCenterServices();
        services.AddXyzAlarmServices();

        return services;
    }
}
