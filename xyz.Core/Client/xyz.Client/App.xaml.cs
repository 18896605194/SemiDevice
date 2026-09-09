using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using xyz.Client.DataModels.Events;
using xyz.Tools;
using xyz.Client.DataModels.Log;
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
