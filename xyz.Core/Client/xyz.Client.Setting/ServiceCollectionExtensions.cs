using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Setting.ViewModels;
using xyz.Client.Setting.Views;

namespace xyz.Client.Setting;

/// <summary>
/// Setting 模块服务注册扩展。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXyzSettingServices(this IServiceCollection services)
    {
        services.AddSingleton<UserViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<UserViewModel>());

        services.AddSingleton<RoleViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<RoleViewModel>());

        services.AddSingleton<MenuViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<MenuViewModel>());

        services.AddKeyedSingleton<UserControl, UserView>("Setting.User");
        services.AddKeyedSingleton<UserControl, RoleView>("Setting.Role");
        services.AddKeyedSingleton<UserControl, MenuView>("Setting.Menu");

        return services;
    }
}
