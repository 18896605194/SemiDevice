using System.IO;
using System.Windows;

namespace SimulatorHub;

/// <summary>
/// xyz 统一仿真器入口: 一个窗口集中跑多台设备仿真 (每类仿真一个 exe 项目, 在这里当页签用)。
/// 可选参数 --data &lt;目录&gt; 指定数据根目录 (instances\&lt;实例名&gt; / profiles\*.json /
/// last-layout.json / crash.log), 默认落在 exe 旁。
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

        // 进程级崩溃钩子, 一进程只此一份 (页签面板不再各自注册)
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
            // 壳构造早期的异常(如 XAML 解析)在这里兜底落盘, 避免无人值守场景静默退场
            File.AppendAllText(Path.Combine(dataDir, "crash.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] 启动异常{Environment.NewLine}{ex}{Environment.NewLine}");
            throw;
        }
    }
}
