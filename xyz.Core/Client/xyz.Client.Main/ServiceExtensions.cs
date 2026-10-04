using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Main.ViewModels;
using xyz.Client.Main.Views;
using xyz.Client.Modules;
using xyz.Shared.Dtos;

namespace xyz.Client.Main;

/// <summary>
/// 主界面模块服务注册扩展。
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// 注册主界面（菜单 Code "Main"）和它中间的默认整机调度。settings 里是后端 sc.xml 装了的 LoadPort、机械手：
    /// 右栏一个 LoadPort 一个页签、默认调度图一台机械手一张都照它生成；后端没起时是空的，页面上显示提示。
    /// 整机调度按 ClientViewKeys.MainDispatch 注册：机型在自己的 IClientModule.Register 里用同一个键再注册一个 UserControl，
    /// 就换成机型的（机型后注册，后注册的生效）。
    /// </summary>
    public static IServiceCollection AddXyzMainServices(this IServiceCollection services, SystemSettingsDto settings)
    {
        services.AddSingleton(_ => new MainPageViewModel(settings.LoadPorts));
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<MainPageViewModel>());
        services.AddKeyedSingleton<UserControl, MainPageView>("Main");

        services.AddKeyedSingleton<UserControl>(ClientViewKeys.MainDispatch, (_, _) => new DispatchView(settings.Robots));

        return services;
    }
}
