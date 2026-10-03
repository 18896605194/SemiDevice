using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc;
using System.Threading.Tasks;
using System.Windows;
using xyz.Client.Common.Alarms;
using xyz.Client.Common.Ec;
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

    /// <summary>
    /// 等后端最多多久。后端要先装配、连完设备才开始监听端口（设备不在线时 TCP 连接要等系统超时），
    /// VS 里前后端一起启动时客户端往往先起来；过了这个时间还没等到，就按后端不在线处理。
    /// </summary>
    private const int BackendWaitSeconds = 30;

    /// <summary>
    /// 后端还没起来时，每隔多久再问一次。
    /// </summary>
    private const int BackendRetryDelayMs = 1000;

    /// <summary>
    /// 主窗口先在屏幕外显示的位置（画完首帧再挪回来）。
    /// </summary>
    private const double OffScreen = -32000;

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

            // 界面语言跟后端 sc.xml 的 System 节点走：先换好语言包再建界面。顺带拿回装了哪些模块、哪些是腔体。
            var settings = await ApplySystemLanguageAsync(loadingWindow);
            await ReportAsync(loadingWindow, 20, L10n.Get("shell.loading.channel"));

            #endregion

            #region 前后端事件的处理初始化

            RemoteEventBus.Initialize();
            ClientAlarms.Initialize();
            ClientEc.Initialize();
            await ReportAsync(loadingWindow, 35, L10n.Get("shell.loading.events"));

            #endregion

            #region 容器服务注册和创建

            var services = new ServiceCollection();
            services.AddXyzClientServices(settings);

            // 机型模块：扫描 Modules 目录里的 [ClientModule] DLL，壳不引用任何机型项目。
            var modules = ClientModuleLoader.Load(services);

            // 机型独有界面的语言包在机型的界面资源程序集里（IClientModule.PresentationAssembly），按当前语言合并。
            foreach (var module in modules)
            {
                var presentationAssembly = module.PresentationAssembly;
                if (presentationAssembly is not null && presentationAssembly.Length > 0)
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
            await ShowMainWindowAsync(loadingWindow);
        }
    }

    /// <summary>
    /// 界面语言跟后端 sc.xml 的 System 节点（Language）走：启动时问一次后端，换好语言包再建界面。
    /// 后端还没起来（连不上）就每秒再问一次，最多等 BackendWaitSeconds——模块表只在这儿拿一次，
    /// 没拿到的话 IO、腔体这些按模块分的菜单整场都是空的。
    /// 等到头还没拿到，或后端在但没配时用默认的简体中文；改了语言重启客户端生效。
    /// </summary>
    private static async Task<SystemSettingsDto> ApplySystemLanguageAsync(LoadingWindow loadingWindow)
    {
        var waitUntil = DateTime.UtcNow.AddSeconds(BackendWaitSeconds);
        while (true)
        {
            try
            {
                var context = new CallContext(new CallOptions(deadline: DateTime.UtcNow.AddSeconds(3)));
                var response = await GrpcClientFactory.Create<ISystemService>().GetSettingsAsync(new RpcRequest(), context);
                var settings = response.DeserializeData<SystemSettingsDto>();
                L10n.Apply(settings.Language);

                // 顺带带回装了哪些模块、哪些是腔体：按模块分的页面与菜单照它生成，sc.xml 里没配的不出现。
                return settings;
            }
            catch (RpcException exception) when (
                exception.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                && DateTime.UtcNow < waitUntil)
            {
                loadingWindow.Report(10, L10n.Get("shell.loading.backend"));
                await Task.Delay(BackendRetryDelayMs);
            }
            catch (Exception exception)
            {
                ClientLog.Warn("Client", $"没拿到界面语言设置，先用 {L10n.Language}：{exception.Message}");
                return new SystemSettingsDto();
            }
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
    /// 全部页面都挂在内容区里（PageHost），主窗口第一次显示时要把它们一起套模板、排版、走"已加载"、画首帧（几秒，界面线程在忙）。
    /// 所以先在屏幕外显示、加载界面置顶开着，等首帧画完再挪回来最大化——用户看到的是加载界面收尾，
    /// 而不是一个看得见却点不动的主窗口；之后点菜单只切可见性，不再现套模板、现排版。
    /// 不在显示前单独预排版：没挂到真窗口上排的那一遍，显示时会整个再排一遍，白费一倍时间。
    /// </summary>
    private async Task ShowMainWindowAsync(LoadingWindow loadingWindow)
    {
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            var rendered = new TaskCompletionSource();
            mainWindow.ContentRendered += (_, _) => rendered.TrySetResult();
            loadingWindow.Topmost = true;

            var workArea = SystemParameters.WorkArea;
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            mainWindow.ShowActivated = false;
            mainWindow.Left = OffScreen;
            mainWindow.Top = OffScreen;
            mainWindow.Width = workArea.Width;
            mainWindow.Height = workArea.Height;
            mainWindow.Show();
            long shown = watch.ElapsedMilliseconds;
            await rendered.Task;
            long firstFrame = watch.ElapsedMilliseconds;

            mainWindow.Left = workArea.Left;
            mainWindow.Top = workArea.Top;
            mainWindow.WindowState = WindowState.Maximized;
            loadingWindow.Close();
            mainWindow.Activate();

            ClientLog.Info("Client", $"主窗口：显示（全部页面套模板、排版）{shown} ms，首帧 {firstFrame - shown} ms");
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
