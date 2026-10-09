using System.Diagnostics;
using xyz.Common.Log;

namespace xyz.Components.Components;

/// <summary>
/// 驱动断线重连（LoadPort、RFID 驱动组件共用）：组件初始化过才管（装机停用、没打开过的不管），连着就什么都不做。
/// 断开那一刻记一次告警，之后每隔给定的间隔在后台重开一次，连上了记一次恢复；中间重连不上不再记日志，免得刷屏。
/// 由所属模块的扫描线程每拍调 Check。重开放在后台做：网口连不上时 Connect 要卡好几秒，不能卡扫描线程；同一时间只有一次重开在跑。
/// </summary>
internal sealed class DriverReconnector
{
    private readonly Stopwatch _watch = new();
    private volatile bool _isEnabled;
    private bool _isDisconnected;
    private Task? _reopening;

    /// <summary>组件初始化了：从现在起断了就重连（这一次连没连上都算）。</summary>
    public void Enable()
    {
        _isEnabled = true;
    }

    /// <summary>组件 Close 了：不再重连。</summary>
    public void Disable()
    {
        _isEnabled = false;
    }

    /// <summary>
    /// 看一眼连接（扫描线程上调）：断了、到点了就在后台跑一次 reopen（先关后开）。source 是记日志用的组件路径。
    /// </summary>
    public void Check(string source, bool isConnected, int intervalMs, Action reopen)
    {
        if (!_isEnabled)
        {
            return;
        }

        if (isConnected)
        {
            if (_isDisconnected)
            {
                _isDisconnected = false;
                LogHelper.Info(source, "通讯恢复");
            }

            return;
        }

        if (!_isDisconnected)
        {
            _isDisconnected = true;
            _watch.Restart();
            LogHelper.Warn(source, $"通讯断开，每 {intervalMs} ms 重连一次，连上之前这一路指令都发不出去");
            return;
        }

        var reopening = _reopening;
        if (reopening is not null && !reopening.IsCompleted)
        {
            return;
        }

        if (_watch.ElapsedMilliseconds < intervalMs)
        {
            return;
        }

        _watch.Restart();
        _reopening = Task.Run(() =>
        {
            try
            {
                reopen();
            }
            catch (Exception exception)
            {
                LogHelper.Debug(source, $"重连没成功：{exception.Message}");
            }
        });
    }
}
