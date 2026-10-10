using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 轴点动（按住动、松开停）：点动指令已经由腔体发出去了，这里等松手——界面松手发停止（腔体转给 <see cref="Release"/>），
/// 或者 EC HoldTimeoutMs 内界面没续上（界面断了、客户端退了），自己发停止。松手后等轴停下。
/// 按住期间腔体一直在"手动中"，别的普通动作发不进来；轴中途报失败先发停止再判失败。
/// 续、松手在 RPC 线程，扫描在模块线程，两边只碰两个原子字段。
/// </summary>
internal sealed class ChamberHoldOperation : ModuleOperation
{
    private const string LogModule = "Chamber";

    private readonly int _holdTimeout;
    private readonly int _releaseTimeout;

    /// <summary>上次续的时刻（本操作计时的毫秒数）。</summary>
    private long _renewedAt;

    /// <summary>已经松手（界面发了停止，或者没续上自己停了）。</summary>
    private volatile bool _released;

    /// <summary>松手的时刻（本操作计时的毫秒数），只在扫描线程里用；-1 = 还没开始等停下。</summary>
    private long _releasedAt = -1;

    /// <param name="axis">点动的轴。</param>
    /// <param name="holdTimeout">多久没续上就自己停（EC HoldTimeoutMs）。</param>
    /// <param name="releaseTimeout">松手后等轴停下的上限（EC DeviceActionTimeout）。</param>
    public ChamberHoldOperation(AxisComponent axis, int holdTimeout, int releaseTimeout)
        : base($"{axis.FullPath} {ChamberDeviceAction.Jog}")
    {
        Axis = axis;
        _holdTimeout = holdTimeout;
        _releaseTimeout = releaseTimeout;
    }

    /// <summary>点动的轴。</summary>
    public AxisComponent Axis { get; }

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

    /// <summary>界面松手了：停止已经由腔体发出去，这里只等轴停下。</summary>
    public void Release()
    {
        _released = true;
    }

    protected override void OnScan()
    {
        string jog = ChamberDeviceAction.Jog.ToString();
        if (!_released)
        {
            if (Axis.ActionState == ActionState.Failed)
            {
                Axis.Stop();
                Fail(ErrorCodes.ChamberDeviceActionFailed, $"{Name}：轴报失败（设备报错）", Axis.FullPath, jog);
                return;
            }

            if (Watch.ElapsedMilliseconds - Interlocked.Read(ref _renewedAt) <= _holdTimeout)
            {
                return;
            }

            LogHelper.Warn(LogModule, $"{Name}：{_holdTimeout}ms 没续上（界面断了或客户端退了），自己停");
            Axis.Stop();
            _released = true;
        }

        if (_releasedAt < 0)
        {
            _releasedAt = Watch.ElapsedMilliseconds;
        }

        switch (Axis.ActionState)
        {
            case ActionState.Completed:
                Complete();
                return;

            case ActionState.Failed:
                Fail(ErrorCodes.ChamberDeviceActionFailed, $"{Name}：松手后轴报失败", Axis.FullPath, jog);
                return;

            case ActionState.Idle:
                Fail(ErrorCodes.Aborted, $"{Name}：轴被中止", Name);
                return;
        }

        if (Watch.ElapsedMilliseconds - _releasedAt > _releaseTimeout)
        {
            Fail(ErrorCodes.ChamberDeviceActionFailed, $"{Name}：松手后等了 {_releaseTimeout}ms 还没停下", Axis.FullPath, jog);
        }
    }
}
