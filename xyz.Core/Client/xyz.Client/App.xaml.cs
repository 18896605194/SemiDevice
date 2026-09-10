using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Windows;
using xyz.Client.Common.Events;
using xyz.Tools;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Modules;
using xyz.Client.Views;

namespace xyz.Client;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 每个加载步骤的停留时长，让进度条看得出来在走。
    /// </summary>
    private const int StepDelayMs = 180;

    public static IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        #region 异常

        RegisterGlobalExceptionHandlers();

        #endregion

        base.OnStartup(e);

        // 先弹加载界面，初始化在后台完成后再切到主界面。
        var loadingWindow = new LoadingWindow();
        loadingWindow.Show();

        _ = StartUpAsync(loadingWindow);
    }

    /// <summary>
    /// 启动初始化：先让加载界面渲染出来，再完成 Grpc 通道、事件流、容器、菜单和 ViewModel 初始化，
    /// 最后切到主界面。初始化失败（例如后端不在线）只记日志，仍然打开主界面，不卡在加载界面。
    /// </summary>
    private async Task StartUpAsync(LoadingWindow loadingWindow)
    {
        try
        {
            await ReportAsync(loadingWindow, 5, "正在启动…");

            #region Grpc的初始化通道

            GrpcClientFactory.Initialize();
            await ReportAsync(loadingWindow, 20, "正在初始化通讯通道…");

            #endregion

            #region 前后端事件的处理初始化

            RemoteEventBus.Initialize();
            await ReportAsync(loadingWindow, 35, "正在连接后端事件流…");

            #endregion

            #region 容器服务注册和创建

            var services = new ServiceCollection();
            services.AddXyzClientServices();

            // 机型模块：扫描 Modules 目录里的 [ClientModule] DLL，壳不引用任何机型项目。
            var modules = ClientModuleLoader.Load(services);

            Services = services.BuildServiceProvider();
            IocHelper.ServiceProvider = Services;
            await ReportAsync(loadingWindow, 55, "正在加载机型模块…");

            #endregion

            // 机型菜单行由机型模块声明，先补齐再让 MainViewModel 读菜单。
            ClientMenuSynchronizer.Ensure(modules);
            await ReportAsync(loadingWindow, 75, "正在同步菜单…");

            #region 加载所有viewmodel 的init

            InitializeViewModels();
            await ReportAsync(loadingWindow, 95, "正在加载界面…");

            #endregion

            await ReportAsync(loadingWindow, 100, "加载完成");
        }
        catch (Exception exception)
        {
            ClientLog.Error("Client", $"启动初始化失败：{exception.Message}");
        }
        finally
        {
            ShowMainWindow(loadingWindow);
        }
    }

    /// <summary>
    /// 更新加载进度并停一小会儿，让进度条看得出来在走。
    /// </summary>
    private static async Task ReportAsync(LoadingWindow loadingWindow, int percent, string message)
    {
        loadingWindow.Report(percent, message);
        await Task.Delay(StepDelayMs);
    }

    /// <summary>
    /// 主界面准备好后显示主窗口并关掉加载界面。
    /// </summary>
    private void ShowMainWindow(LoadingWindow loadingWindow)
    {
        try
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            loadingWindow.Close();
        }
        catch (Exception exception)
        {
            // 主界面都打不开时，日志下拉框没人看得到——ClientLog 已经落文件，直接查 Log\client-*.log。
            ClientLog.Error("Client", $"打开主界面失败：{exception.Message}");
        }
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
