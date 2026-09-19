using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc;
using System.Threading.Tasks;
using System.Windows;
using xyz.Client.Common.Alarms;
using xyz.Client.Common.Events;
using xyz.Tools;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Modules;
using xyz.Client.Presentation.Localization;
using xyz.Client.Views;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;

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
    /// 启动初始化：先让加载界面渲染出来，再完成 Grpc 通道、事件流、容器和 ViewModel 初始化，
    /// 最后切到主界面。初始化失败（例如后端不在线）只记日志，仍然打开主界面，不卡在加载界面。
    /// </summary>
    private async Task StartUpAsync(LoadingWindow loadingWindow)
    {
        try
        {
            await ReportAsync(loadingWindow, 5, L10n.Get("shell.loading.starting"));

            #region Grpc的初始化通道

            GrpcClientFactory.Initialize();

            // 界面语言跟后端 sc.xml 的 System 节点走：先换好语言包再建界面。
            await ApplySystemLanguageAsync();
            await ReportAsync(loadingWindow, 20, L10n.Get("shell.loading.channel"));

            #endregion

            #region 前后端事件的处理初始化

            RemoteEventBus.Initialize();
            ClientAlarms.Initialize();
            await ReportAsync(loadingWindow, 35, L10n.Get("shell.loading.events"));

            #endregion

            #region 容器服务注册和创建

            var services = new ServiceCollection();
            services.AddXyzClientServices();

            // 机型模块：扫描 Modules 目录里的 [ClientModule] DLL，壳不引用任何机型项目。
            var modules = ClientModuleLoader.Load(services);

            // 机型独有界面的语言包在机型的界面资源程序集里（IClientModule.PresentationAssembly），按当前语言合并。
            foreach (var module in modules)
            {
                if (module.PresentationAssembly is { Length: > 0 } presentationAssembly)
                {
                    L10n.AddPack(presentationAssembly);
                }
            }

            Services = services.BuildServiceProvider();
            IocHelper.ServiceProvider = Services;
            await ReportAsync(loadingWindow, 55, L10n.Get("shell.loading.modules"));

            #endregion

            #region 加载所有viewmodel 的init

            InitializeViewModels();
            await ReportAsync(loadingWindow, 95, L10n.Get("shell.loading.views"));

            #endregion

            await ReportAsync(loadingWindow, 100, L10n.Get("shell.loading.done"));
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
    /// 界面语言跟后端 sc.xml 的 System 节点（Language）走：启动时问一次后端，换好语言包再建界面。
    /// 后端没起或没配时用默认的简体中文；改了语言重启客户端生效。
    /// </summary>
    private static async Task ApplySystemLanguageAsync()
    {
        try
        {
            var context = new CallContext(new CallOptions(deadline: DateTime.UtcNow.AddSeconds(3)));
            var response = await GrpcClientFactory.Create<ISystemService>().GetSettingsAsync(new RpcRequest(), context);
            L10n.Apply(response.DeserializeData<SystemSettingsDto>().Language);
        }
        catch (Exception exception)
        {
            ClientLog.Warn("Client", $"没拿到界面语言设置，先用 {L10n.Language}：{exception.Message}");
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

    /// <summary>
    /// 逐个 Init：一个页面失败（多半是后端没起）只记日志，不耽误后面的，顶栏的灯和时间照常走。
    /// </summary>
    private static void InitializeViewModels()
    {
        foreach (var viewModel in Services.GetServices<BaseViewModel>())
        {
            try
            {
                viewModel.Init();
            }
            catch (Exception exception)
            {
                ClientLog.Error("Client", $"{viewModel.GetType().Name} 初始化失败：{exception.Message}");
            }
        }
    }
}
