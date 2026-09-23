using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace xyz.Client.Presentation.Helpers;

/// <summary>
/// 系统标题栏跟深色界面一致：让 Windows 把标题栏（连同系统自带的最小化 / 最大化 / 关闭）画成深色。
/// Win10 1809 起支持；系统不支持时什么都不做，保持默认标题栏。
/// </summary>
public static class DarkTitleBar
{
    /// <summary>
    /// DWMWA_USE_IMMERSIVE_DARK_MODE（Win10 20H1 起）。
    /// </summary>
    private const int UseImmersiveDarkMode = 20;

    /// <summary>
    /// 20H1 之前同一个属性的编号。
    /// </summary>
    private const int UseImmersiveDarkModeBefore20H1 = 19;

    /// <summary>
    /// 窗口句柄建好之后调（OnSourceInitialized 里）。
    /// </summary>
    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
