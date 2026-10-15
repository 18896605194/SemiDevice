using xyz.Common.Log;

namespace xyz.Components.Components;

/// <summary>
/// 断线重连（PLC、LoadPort、RFID、机械手驱动组件共用）：组件初始化过才管（装机停用、没打开过的不管），连着就什么都不做。
/// 断开那一刻记一次告警，之后每隔给定的间隔在后台重开一次，最多重开给定的次数；还连不上就停下（Check 返回 true，组件拿它报警），
/// 人工复位时 Rearm 从头再来。连上以后连着满一个间隔才算恢复（记一次、次数清零）——串口这类打开就算连上的，设备不在要等发东西失败才断，
/// 连上一下又断的照样算一次重连，不然次数一直清零、永远停不下来。中间重连不上不再记日志，免得刷屏。
/// 由所属组件的扫描线程每拍调 Check。重开放在后台做：网口连不上时 Connect 要卡好几秒，不能卡扫描线程；同一时间只有一次重开在跑。
/// </summary>
internal sealed class DriverReconnector
{
    private readonly object _gate = new();
    private volatile bool _isEnabled;
    private bool _isDisconnected;
    private bool _hasGivenUp;
    private int _attempts;
    private long _nextAttemptAt;

    /// <summary>断着的时候重连上了是什么时刻（0 = 没连着）：连满一个间隔才算恢复。</summary>
    private long _connectedSince;

    private Task? _reopening;

    /// <summary>组件初始化了：从现在起断了就重连（这一次连没连上都算），次数从头算。</summary>
    public void Enable()
    {
        Rearm();
        _isEnabled = true;
    }

    /// <summary>组件 Close 了：不再重连。</summary>
    public void Disable()
    {
        _isEnabled = false;
    }

    /// <summary>
    /// 人工复位：次数清零，停下来的接着连，而且马上就连（不等一个间隔）。连着的时候调也没关系。
    /// </summary>
    public void Rearm()
    {
        lock (_gate)
        {
            _attempts = 0;
            _hasGivenUp = false;
            _nextAttemptAt = 0;
        }
    }

    /// <summary>
    /// 看一眼连接（扫描线程上调）：断了、到点了就在后台跑一次 reopen（先关后开）；重开满 maxCount 次还没连上就停下（0 = 不自动重连）。
    /// source 是记日志用的组件路径。返回 true 表示这一拍刚停下来（只返回这一次），组件拿它报"已停止重连"。
    /// </summary>
    public bool Check(string source, bool isConnected, int intervalMs, int maxCount, Action reopen)
    {
        if (!_isEnabled)
        {
            return false;
        }

        lock (_gate)
        {
            long now = Environment.TickCount64;
            if (isConnected)
            {
                if (!_isDisconnected)
                {
                    return false;
                }

                if (_connectedSince == 0)
                {
                    _connectedSince = now;
                }

                if (now - _connectedSince < intervalMs)
                {
                    return false;
                }

                _isDisconnected = false;
                _connectedSince = 0;
                _attempts = 0;
                _hasGivenUp = false;
                LogHelper.Info(source, "通讯恢复");
                return false;
            }

            _connectedSince = 0;
            if (!_isDisconnected)
            {
                _isDisconnected = true;
                _nextAttemptAt = now + intervalMs;
                LogHelper.Warn(source, $"通讯断开，每 {intervalMs} ms 重连一次、最多 {maxCount} 次，连上之前这一路指令都发不出去");
                return false;
            }

            var reopening = _reopening;
            if (_hasGivenUp || (reopening is not null && !reopening.IsCompleted))
            {
                return false;
            }

            if (_attempts >= maxCount)
            {
                _hasGivenUp = true;
                LogHelper.Warn(source, $"重连 {_attempts} 次都没连上，不再重连；查好线路、设备后人工复位，复位会重新开始连");
                return true;
            }

            if (now < _nextAttemptAt)
            {
                return false;
            }

            _nextAttemptAt = now + intervalMs;
            _attempts++;
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
            return false;
        }
    }
}
