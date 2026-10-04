using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Enums;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 按住类部件动作（点动）：指令已经由腔体发出去了，这里等松手——界面松手发停止（腔体转给 <see cref="Release"/>），
/// 或者 EC HoldTimeoutMs 内界面没续上（界面断了、客户端退了），自己发松手动作。松手后等部件把松手动作做完。
/// 按住期间腔体一直在"手动中"，别的普通动作发不进来；部件中途报失败（轴报错）先发松手动作再判失败。
/// 续、松手在 RPC 线程，扫描在模块线程，两边只碰两个原子字段。
/// </summary>
internal sealed class ChamberHoldOperation : ModuleOperation
{
    private const string LogModule = "Chamber";

    private readonly Func<bool> _release;
    private readonly int _holdTimeout;
    private readonly int _releaseTimeout;

    /// <summary>上次续的时刻（本操作计时的毫秒数）。</summary>
    private long _renewedAt;

    /// <summary>已经松手（界面发了停止，或者没续上自己停了）。</summary>
    private volatile bool _released;

    /// <summary>松手的时刻（本操作计时的毫秒数），只在扫描线程里用；-1 = 还没开始等松手动作。</summary>
    private long _releasedAt = -1;

    /// <param name="part">按住的部件。</param>
    /// <param name="action">按住类动作名（如 Jog）。</param>
    /// <param name="release">发松手动作（如 Stop）；返回指令发没发出去。</param>
    /// <param name="holdTimeout">多久没续上就自己松手（EC HoldTimeoutMs）。</param>
    /// <param name="releaseTimeout">松手后等松手动作做完的上限（EC PartActionTimeout）。</param>
    public ChamberHoldOperation(ComponentBase part, string action, Func<bool> release, int holdTimeout, int releaseTimeout)
        : base($"{part.FullPath} {action}")
    {
        Part = part;
        Action = action;
        _release = release;
        _holdTimeout = holdTimeout;
        _releaseTimeout = releaseTimeout;
    }

    /// <summary>按住的部件。</summary>
    public ComponentBase Part { get; }

    /// <summary>按住类动作名。</summary>
    public string Action { get; }

    /// <summary>界面还按着：重新计时；已经松手或结束了返回 false（界面就不用再续了）。</summary>
    public bool Renew()
    {
        if (_released || IsTerminal)
        {
            return false;
        }

        Interlocked.Exchange(ref _renewedAt, Watch.ElapsedMilliseconds);
        return true;
    }

    /// <summary>界面松手了：松手动作（停止）已经由腔体发出去，这里只等它做完。</summary>
    public void Release()
    {
        _released = true;
    }

    protected override void OnScan()
    {
        if (!_released)
        {
            if (ChamberPartOperation.ActionStateOf(Part) == ActionState.Failed)
            {
                _release();
                Fail(ErrorCodes.ChamberPartActionFailed, $"{Name}：部件报失败（设备报错）", Part.FullPath, Action);
                return;
            }

            if (Watch.ElapsedMilliseconds - Interlocked.Read(ref _renewedAt) <= _holdTimeout)
            {
                return;
            }

            LogHelper.Warn(LogModule, $"{Name}：{_holdTimeout}ms 没续上（界面断了或客户端退了），自己停");
            _release();
            _released = true;
        }

        if (_releasedAt < 0)
        {
            _releasedAt = Watch.ElapsedMilliseconds;
        }

        switch (ChamberPartOperation.ActionStateOf(Part))
        {
            case ActionState.Completed:
                Complete();
                return;

            case ActionState.Failed:
                Fail(ErrorCodes.ChamberPartActionFailed, $"{Name}：松手后部件报失败", Part.FullPath, Action);
                return;

            case ActionState.Idle:
                Fail(ErrorCodes.Aborted, $"{Name}：部件被中止", Name);
                return;
        }

        if (Watch.ElapsedMilliseconds - _releasedAt > _releaseTimeout)
        {
            Fail(ErrorCodes.ChamberPartActionFailed, $"{Name}：松手后等了 {_releaseTimeout}ms 还没停下", Part.FullPath, Action);
        }
    }
}
