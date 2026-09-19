using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.DataCenter.ViewModels;
using xyz.Client.DataCenter.Views;
using xyz.Client.DataModels.ViewModels;

namespace xyz.Client.DataCenter;

/// <summary>
/// DataCenter 模块服务注册扩展：实时日志、日志历史两个页面（报警的两个页面在 xyz.Client.Alarm）。
/// 页面按菜单 Code 注册（菜单在壳的 PlatformMenuProvider 里声明）。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXyzDataCenterServices(this IServiceCollection services)
    {
        services.AddSingleton<LogRealtimeViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<LogRealtimeViewModel>());

        services.AddSingleton<LogHistoryViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<LogHistoryViewModel>());

        services.AddKeyedSingleton<UserControl, LogRealtimeView>("DataCenter.LogRealtime");
        services.AddKeyedSingleton<UserControl, LogHistoryView>("DataCenter.LogHistory");

        return services;
    }
}
