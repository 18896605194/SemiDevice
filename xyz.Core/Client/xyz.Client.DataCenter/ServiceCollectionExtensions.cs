using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.DataCenter.ViewModels;
using xyz.Client.DataCenter.Views;
using xyz.Client.DataModels.ViewModels;

namespace xyz.Client.DataCenter;

/// <summary>
/// DataCenter 模块服务注册扩展：实时日志、日志历史、实时报警、报警历史四个页面。
/// 页面按菜单 Code 注册（菜单行由后端 MenuService 种子补齐）。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXyzDataCenterServices(this IServiceCollection services)
    {
        services.AddSingleton<LogRealtimeViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<LogRealtimeViewModel>());

        services.AddSingleton<LogHistoryViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<LogHistoryViewModel>());

        services.AddSingleton<AlarmRealtimeViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<AlarmRealtimeViewModel>());

        services.AddSingleton<AlarmHistoryViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<AlarmHistoryViewModel>());

        services.AddKeyedSingleton<UserControl, LogRealtimeView>("DataCenter.LogRealtime");
        services.AddKeyedSingleton<UserControl, LogHistoryView>("DataCenter.LogHistory");
        services.AddKeyedSingleton<UserControl, AlarmRealtimeView>("Alarm.Realtime");
        services.AddKeyedSingleton<UserControl, AlarmHistoryView>("Alarm.History");

        return services;
    }
}
