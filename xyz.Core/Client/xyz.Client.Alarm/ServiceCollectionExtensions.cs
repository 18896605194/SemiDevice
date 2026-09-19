using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.Alarm.ViewModels;
using xyz.Client.Alarm.Views;
using xyz.Client.DataModels.ViewModels;

namespace xyz.Client.Alarm;

/// <summary>
/// Alarm 模块服务注册扩展：实时报警、报警历史两个页面。
/// 页面按菜单 Code 注册（菜单在壳的 PlatformMenuProvider 里声明）。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXyzAlarmServices(this IServiceCollection services)
    {
        services.AddSingleton<AlarmRealtimeViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<AlarmRealtimeViewModel>());

        services.AddSingleton<AlarmHistoryViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<AlarmHistoryViewModel>());

        services.AddKeyedSingleton<UserControl, AlarmRealtimeView>("Alarm.Realtime");
        services.AddKeyedSingleton<UserControl, AlarmHistoryView>("Alarm.History");

        return services;
    }
}
