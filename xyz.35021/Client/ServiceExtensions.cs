using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Manual;
using xyz.Client.Presentation.Controls.ThreeD;
using ProjectChamberViewModel = xyz._35021.Client.Manual.ViewModels.ChamberManualViewModel;

using xyz._35021.Client.Manual.ViewModels;
using xyz._35021.Client.Manual.Views;

namespace xyz._35021.Client;

/// <summary>
/// 35021 机型模块的客户端服务注册：页面按菜单 Code 注册成 keyed UserControl，
/// 平台主界面按菜单 Code 从 DI 取页面塞进中间内容区。
/// </summary>
public static class ServiceExtensions
{
    public static IServiceCollection AddXyz35021ClientServices(this IServiceCollection services)
    {
        // 大手动界面：一个页面里按 machine.xml 配的 LoadPort 数量动态生成多个面板。
        services.AddSingleton<LoadPortsManualViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<LoadPortsManualViewModel>());

        services.AddKeyedSingleton<UserControl, LoadPortsManualView>("Manual.LoadPorts");

        // Robot 手动界面：一个页面里按 machine.xml 配的 Robot 数量动态生成多个面板。
        services.AddSingleton<RobotManualViewModel>();
        services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredService<RobotManualViewModel>());

        services.AddKeyedSingleton<UserControl, RobotManualView>("Manual.Robot");

        // 项目可在此注册 IChamberVisualProvider；已提供的种类优先，剩余种类使用平台控件。
        foreach (var provider in ChamberVisualProviderLoader.FromAssembly(typeof(Presentation.Controls.ChamberView).Assembly))
            services.AddSingleton<IChamberVisualProvider>(provider);
        services.AddSingleton(sp => new ChamberVisualCatalog(sp.GetServices<IChamberVisualProvider>()));
        var chambers = services.Select(d => d.ImplementationInstance).OfType<ChamberManualPageRegistration>().ToArray();
        foreach (var chamber in chambers)
        {
            string module = chamber.Module;
            services.AddKeyedSingleton<ProjectChamberViewModel>(module,
                (sp, _) => new ProjectChamberViewModel(module, sp.GetRequiredService<ChamberVisualCatalog>()));
            services.AddSingleton<BaseViewModel>(sp => sp.GetRequiredKeyedService<ProjectChamberViewModel>(module));
            services.AddKeyedSingleton<UserControl>($"Manual.{module}", (_, _) => new ChamberManualView(module));
        }
        return services;
    }
}
