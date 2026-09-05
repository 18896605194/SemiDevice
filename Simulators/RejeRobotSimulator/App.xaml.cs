using System.IO;
using System.Windows;

namespace RejeRobotSimulator;

/// <summary>
/// RejeRobot 机械手仿真器入口。
/// 可选参数 --data &lt;目录&gt; 指定本实例的数据目录（config.json / fault.* 旗标 / log.txt），
/// 同机跑多实例（Robot1/Robot2）时各给一个目录互不干扰；默认落在 exe 旁。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string dataDir = AppContext.BaseDirectory;
        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            if (e.Args[i] is "--data" or "-d")
            {
                dataDir = Path.GetFullPath(e.Args[i + 1]);
                break;
            }
        }
        Directory.CreateDirectory(dataDir);

        // 进程级崩溃钩子 (原先在 MainWindow 里注册, 面板化后上移至此, 一进程只此一份)
        DispatcherUnhandledException += (_, e) =>
        {
            File.AppendAllText(Path.Combine(dataDir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] UI线程异常{Environment.NewLine}{e.Exception}{Environment.NewLine}");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(dataDir, "crash.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] 后台线程异常{Environment.NewLine}{e.ExceptionObject}{Environment.NewLine}");
            }
            catch { /* 进程正在退出, 忽略 */ }
        };

        try
        {
            var window = new MainWindow(dataDir);
            window.Show();
        }
        catch (Exception ex)
        {
            // MainWindow 构造早期的异常(如 XAML 解析)发生在其内部崩溃处理器注册之前,
            // 在这里兜底落盘, 避免无人值守场景静默退场
            File.AppendAllText(Path.Combine(dataDir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] 启动异常{Environment.NewLine}{ex}{Environment.NewLine}");
            throw;
        }
    }
}
