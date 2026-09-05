using Microsoft.Extensions.DependencyInjection;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Io;
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
        services.AddTransient<PlaceholderView>();

        services.AddXyzSettingServices();
        services.AddXyzIoServices();

        return services;
    }
}
