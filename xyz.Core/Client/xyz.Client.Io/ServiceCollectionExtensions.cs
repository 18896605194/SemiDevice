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
    /// <summary>
    /// 注册一个模块的 IO 页面。模块有几个、叫什么由机型定（跟 sc.xml 里的模块名对齐），
    /// 机型每个模块调一次，页面按菜单 Code "Io.&lt;模块名&gt;" 注册。
    ///
    /// 页面内容（哪些点、几类点）仍然由后端推的点表决定——点表里给这个模块加一行，界面上就多一行。
    /// </summary>
    public static IServiceCollection AddXyzIoModule(this IServiceCollection services, string module)
    {
        string code = $"Io.{module}";

        services.AddKeyedSingleton(code, (IServiceProvider _, object? _) => new IoViewModel(module));

        // 注册成 BaseViewModel，壳启动时统一 Init——订阅就是在那儿起来的。
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredKeyedService<IoViewModel>(code));

        services.AddKeyedSingleton<UserControl>(code,
            (sp, key) => new IoView(sp.GetRequiredKeyedService<IoViewModel>(key)));

        return services;
    }
}
