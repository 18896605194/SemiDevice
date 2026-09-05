using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Io.ViewModels;
using xyz.Client.Io.Views;

namespace xyz.Client.Io;

/// <summary>
/// IO 模块服务注册扩展。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXyzIoServices(this IServiceCollection services)
    {
        services.AddSingleton<IoViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<IoViewModel>());

        services.AddKeyedSingleton<UserControl, IoView>("Io");

        return services;
    }
}
