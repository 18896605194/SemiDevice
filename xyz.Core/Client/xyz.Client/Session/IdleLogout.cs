using System.Runtime.InteropServices;
using System.Windows.Threading;
using xyz.Client.Common.Session;

namespace xyz.Client.Session;

/// <summary>
/// 闲置自动退出：键盘鼠标多久没动（会话带的分钟数，账号组件 machine.xml 的 IdleLogoutMinutes）就退出登录，接着由顶栏弹登录框要重新登录。
/// 看的是 Windows 记的最后一次有人碰键盘、鼠标、触摸屏的时刻（整台电脑的），不看 WPF 自己的输入事件：
/// 鼠标停在窗口上时，界面一刷新（顶栏时钟每秒走一下）WPF 就补发一次"鼠标移动"，那样永远算不上闲着
/// </summary>
public static class IdleLogout
{
    /// <summary>
    /// 多久看一次闲置了多久
    /// </summary>
    private const int CheckIntervalSeconds = 10;

    private const long MillisecondsPerMinute = 60000;

    private static DispatcherTimer? _timer;

    /// <summary>
    /// 现在登录着的是谁：换了人才重新计时，同一个人的会话刷新（权限变了、EC 变了）不算
    /// </summary>
    private static string _loginName = string.Empty;

    /// <summary>
    /// 这个人登录的时刻（开机以来的毫秒数）：闲置从这儿起算，不算登录之前闲着的时间
    /// </summary>
    private static long _loggedInAt = Environment.TickCount64;

    /// <summary>
    /// 闲置到点、已经退出登录以后发（界面线程上）：带刚才那个人的登录名和分钟数，顶栏接着弹登录框
    /// </summary>
    public static event Action<string, int>? TimedOut;

    /// <summary>
    /// 主窗口起来以后调一次（在界面线程上）
    /// </summary>
    public static void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        ClientSession.Changed += OnSessionChanged;
        OnSessionChanged();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CheckIntervalSeconds) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void OnSessionChanged()
    {
        string loginName = ClientSession.LoginName;
        if (loginName == _loginName)
        {
            return;
        }

        _loginName = loginName;
        _loggedInAt = Environment.TickCount64;
    }

    private static async void OnTick(object? sender, EventArgs e)
    {
        int minutes = ClientSession.IdleLogoutMinutes;
        if (!ClientSession.IsLoggedIn || minutes <= 0)
        {
            return;
        }

        long idle = Math.Min(IdleMilliseconds(), Environment.TickCount64 - _loggedInAt);
        if (idle < minutes * MillisecondsPerMinute)
        {
            return;
        }

        // 退出时会话当场就回访客，下一拍不会再进来
        string userName = ClientSession.LoginName;
        await ClientAccount.LogoutAsync(isIdle: true);
        TimedOut?.Invoke(userName, minutes);
    }

    /// <summary>
    /// 离最后一次键盘、鼠标、触摸过了多少毫秒；问不到当刚动过
    /// </summary>
    private static long IdleMilliseconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return 0;
        }

        // 两个都是开机以来毫秒数的低 32 位，按无符号相减：开机超过 49.7 天绕回去也对
        return unchecked((uint)Environment.TickCount - info.Time);
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
