using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using xyz.Client.Common.Events;
using xyz.Tools;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
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
        RegisterGlobalExceptionHandlers();

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

    /// <summary>
    /// 三个全局异常兜底，全部写进日志下拉框：
    /// ① UI 线程未处理异常——拦下来，不带崩界面；
    /// ② 非 UI 线程未处理异常——进程会终止，至少留下日志；
    /// ③ 未观察的 Task 异常——标记为已观察，不让它升级成致命异常。
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ClientLog.Error("Client", args.Exception.Message);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var message = args.ExceptionObject as Exception;
            ClientLog.Error("Client", message?.Message ?? args.ExceptionObject?.ToString() ?? "未知异常");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ClientLog.Error("Client", args.Exception.Message);
            args.SetObserved();
        };
    }

    private static void InitializeViewModels()
    {
        foreach (var viewModel in Services.GetServices<BaseViewModel>())
        {
            viewModel.Init();
        }
    }
}
