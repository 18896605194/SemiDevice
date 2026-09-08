using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using xyz.Client.DataModels.Events;
using xyz.Client.DataModels.Ioc;
using xyz.Client.DataModels.Rpc;
using xyz.Client.DataModels.ViewModels;

namespace xyz.Client;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局兜底：命令内的异步异常（如后端不在线）不再带崩界面；后续接入报警中心
        DispatcherUnhandledException += (_, args) => args.Handled = true;

        GrpcClientFactory.Initialize();
        RemoteEventBus.Initialize();

        var services = new ServiceCollection();
        services.AddXyzClientServices();

        Services = services.BuildServiceProvider();
        IocHelper.ServiceProvider = Services;

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
