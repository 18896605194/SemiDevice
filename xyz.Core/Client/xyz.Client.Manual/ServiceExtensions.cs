using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.Manual.Views;

namespace xyz.Client.Manual;

/// <summary>
/// 手动模块服务注册扩展。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册一个腔体的手动页面，按菜单 Code "Manual.&lt;模块名&gt;" 注册，跟 Manual 下的腔体子菜单一一对应。
    /// 腔体有几个、叫什么由后端 sc.xml 定，壳按系统设置里的腔体表每个调一次——四个腔体一模一样也是四页。
    /// 机型要换成自己的腔体页，按同一个 Code 再注册一次即可（后注册的生效）。
    /// </summary>
    public static IServiceCollection AddXyzChamberManual(this IServiceCollection services, string module)
    {
        services.AddKeyedSingleton<UserControl>($"Manual.{module}",
            (_, _) => new ChamberManualControl { ModuleName = module });

        return services;
    }
}
