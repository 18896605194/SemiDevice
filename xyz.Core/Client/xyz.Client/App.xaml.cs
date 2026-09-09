using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using xyz.Client.DataModels.Events;
using xyz.Tools;
using xyz.Client.DataModels.Log;
using xyz.Client.DataModels.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Modules;

namespace xyz.Client;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局兜底：命令内未处理的异常（如后端不在线）写进日志下拉框，不带崩界面。
        DispatcherUnhandledException += (_, args) =>
        {
            ClientLog.Error("Client", args.Exception.Message);
            args.Handled = true;
        };

        GrpcClientFactory.Initialize();
        RemoteEventBus.Initialize();

        var services = new ServiceCollection();
        services.AddXyzClientServices();

        // 机型模块：扫描 Modules 目录里的 [ClientModule] DLL，壳不引用任何机型项目。
        var modules = ClientModuleLoader.Load(services);

        Services = services.BuildServiceProvider();
        IocHelper.ServiceProvider = Services;

        // 机型菜单行由机型模块声明，先补齐再让 MainViewModel 读菜单。
        ClientMenuSynchronizer.Ensure(modules);

        InitializeViewModels();

        base.OnStartup(e);
    }

    private static void InitializeViewModels()
    {
        foreach (var viewModel in Services.GetServices<BaseViewModel>())
        {
            viewModel.Init();
        }
    }
}
