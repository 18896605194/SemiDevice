using System.Diagnostics;
using xyz.Common.Log;
using xyz.Elements.Interfaces;
using xyz.Drivers.Device;

namespace xyz.Elements.Elements;

/// <summary>
/// 设备状态轮询的节奏：一圈一圈地查，同一时间只有一条在途，超时就作废接着查下一条；一圈查完，离这一圈开始满一个周期再开下一圈。
/// 定时查状态一律走它：它发的指令打上 IsPolling，通道把这些收发原文记到轮询文件，不进主日志
/// </summary>
public sealed class StatusQueryLoop
{
    private readonly ILogSource _owner;
    private readonly Stopwatch _queryWatch = new();
    private readonly Stopwatch _roundWatch = new();

    /// <summary>
    /// 这一圈查到哪了；一圈查完为 null
    /// </summary>
    private IEnumerator<DeviceCommand>? _round;

    /// <summary>
    /// 在途的那条查询；发不出去（被拒）的不算在途
    /// </summary>
    private DeviceCommand? _query;

    private bool _isLate;
    private bool _wasConnected;

    /// <param name="owner">日志记在谁名下（做轮询的模块）。</param>
    public StatusQueryLoop(ILogSource owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// 推一拍（模块扫描线程上调）：没连上丢掉这一圈；在途那条没回来、没超时就等，超时作废；回来了就往下走一步——
    /// 一圈查什么、回来写到哪由 queryDataLoop 给（每圈新取一次，yield return 一条 = 替它发出去、等它回来或超时作废）。
    /// 返回这一拍出了什么事（超时、断线），查到的值不从这里出来
    /// </summary>
    public StatusQueryEvent Scan(ICommDriver driver, Func<IEnumerable<DeviceCommand>> queryDataLoop, int intervalMs, int timeoutMs)
    {
        #region 没连上：这一圈作废

        if (!driver.IsConnected)
        {
            _round = null;
            _query = null;
            if (_wasConnected)
            {
                _wasConnected = false;
                return StatusQueryEvent.Disconnected;
            }

            return StatusQueryEvent.None;
        }

        _wasConnected = true;

        #endregion

        #region 等在途那条：没回来、没超时就等；超时作废

        var happened = StatusQueryEvent.None;
        var query = _query;
        if (query is not null && !query.IsCompleted)
        {
            if (_queryWatch.ElapsedMilliseconds < timeoutMs)
            {
                return StatusQueryEvent.None;
            }

            // 回复丢了的话这条会一直占着在途位，同名查询再也发不出去，所以要作废
            driver.Abandon(query, "Timeout");
            happened = StatusQueryEvent.TimedOut;
            if (!_isLate)
            {
                _isLate = true;
                LogHelper.Warn(_owner, $"设备状态查询超时（{timeoutMs}ms）：这一条作废、接着查");
            }
        }
        else if (_isLate && query is not null && query.IsSuccess)
        {
            _isLate = false;
            LogHelper.Info(_owner, "设备状态查询恢复");
        }

        #endregion

        #region 往下走一步：上一条的结果由 queryDataLoop 自己写，接着发这一圈的下一条

        // 新的一圈离上一圈开始不满一个周期就先不查；一圈里的各条照旧一条回来接着发下一条
        if (_round is null)
        {
            if (_roundWatch.IsRunning && _roundWatch.ElapsedMilliseconds < intervalMs)
            {
                return happened;
            }

            _round = queryDataLoop().GetEnumerator();//每次执行拿一次结果
            _roundWatch.Restart();
        }

        if (_round.MoveNext())
        {
            var next = _round.Current;
            _query = null;
            // 状态轮询发的：通道看到这个标记，把收发原文记到轮询文件
            next.IsPolling = true;
            if (driver.Send(next))
            {
                _query = next;
            }

            _queryWatch.Restart();
            return happened;
        }

        // 一圈查完：满一个周期再从头查
        _round = null;
        _query = null;
        return happened;

        #endregion
    }
}

/// <summary>
/// 状态轮询这一拍出了什么事（模块据此清状态）；查到的值由模块的 QueryDataLoop 自己写，不从这里出来
/// </summary>
public enum StatusQueryEvent
{
    /// <summary>
    /// 没出事：在等，或者往下走了一步
    /// </summary>
    None,

    /// <summary>
    /// 在途那条超时作废了
    /// </summary>
    TimedOut,

    /// <summary>
    /// 连接刚断（断开那一拍报一次）
    /// </summary>
    Disconnected,
}
