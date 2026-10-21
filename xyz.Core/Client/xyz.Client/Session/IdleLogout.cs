using System.Windows.Input;
using System.Windows.Threading;
using xyz.Client.Common.Session;

namespace xyz.Client.Session;

/// <summary>
/// 闲置自动注销：键盘鼠标多久没动（会话带的分钟数，账号组件的 EC）就注销回访客
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
    /// 上一次有键盘、鼠标、触摸的时刻（开机以来的毫秒数）
    /// </summary>
    private static long _lastInputAt = Environment.TickCount64;

    /// <summary>
    /// 主窗口起来以后调一次（在界面线程上）
    /// </summary>
    public static void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        InputManager.Current.PreProcessInput += OnInput;
        ClientSession.Changed += OnSessionChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CheckIntervalSeconds) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void OnInput(object sender, PreProcessInputEventArgs e)
    {
        _lastInputAt = Environment.TickCount64;
    }

    /// <summary>
    /// 刚登录、换人的那一刻重新计时
    /// </summary>
    private static void OnSessionChanged()
    {
        _lastInputAt = Environment.TickCount64;
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        int minutes = ClientSession.IdleLogoutMinutes;
        if (!ClientSession.IsLoggedIn || minutes <= 0)
        {
            return;
        }

        if (Environment.TickCount64 - _lastInputAt < minutes * MillisecondsPerMinute)
        {
            return;
        }

        _ = ClientAccount.LogoutAsync(isIdle: true);
    }
}
