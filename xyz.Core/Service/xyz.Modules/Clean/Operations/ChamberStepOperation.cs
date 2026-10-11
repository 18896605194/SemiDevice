using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 整腔动作（回零、复位、中止、工艺）的底座：一段一段往下走，每段同时给几个设备发指令，等它们都做完再走下一段。
/// 指令没发出去、设备报失败（到位超时、轴报错、PLC 断了）、设备被中止，或者整个动作超过 EC 超时，就失败。
/// 只在模块扫描线程里推进（模块在锁里扫它），这时设备的状态是组件扫描刚刷新过的。
/// </summary>
internal abstract class ChamberStepOperation : ModuleOperation
{
    private readonly string _module;
    private readonly int _timeout;

    /// <summary>这一段在等的设备、发给它的动作、是不是这里发的。</summary>
    private readonly List<(ComponentBase Device, ChamberDeviceAction Action, bool Sent)> _waiting = [];

    /// <param name="name">操作名（日志用），如 "Chamber1 Home"。</param>
    /// <param name="module">腔体模块名（超时的错误码参数）。</param>
    /// <param name="timeout">整个动作的上限（EC），ms。</param>
    protected ChamberStepOperation(string name, string module, int timeout) : base(name)
    {
        _module = module;
        _timeout = timeout;
    }

    /// <summary>
    /// 发一条设备指令，这一段等它做完；没发出去就失败、返回 false（调用方直接 return）。
    /// </summary>
    protected bool Send(ComponentBase device, ChamberDeviceAction action, Func<bool> command)
    {
        if (!command())
        {
            Fail(ErrorCodes.ChamberDeviceCommandRejected, $"{Name}：{device.FullPath} {action} 指令没发出去", device.FullPath, action.ToString());
            return false;
        }

        _waiting.Add((device, action, true));
        return true;
    }

    /// <summary>
    /// 指令别处已经发了（中止、复位时组件自己停轴、清错），这一段只等它做完；设备手上没有动作（Idle，从没发过指令）算做完。
    /// </summary>
    protected void Await(ComponentBase device, ChamberDeviceAction action)
    {
        _waiting.Add((device, action, false));
    }

    protected sealed override void OnScan()
    {
        if (Watch.ElapsedMilliseconds > _timeout)
        {
            Fail(ErrorCodes.ChamberActionTimeout, $"{Name}：超过 {_timeout}ms 还没做完", _module, _timeout.ToString());
            return;
        }

        OnWaiting();
        if (IsTerminal || !Settle())
        {
            return;
        }

        Advance();
    }

    /// <summary>每拍在判这一段做没做完之前调：工艺扫描到点了要在半路停轴，就在这儿发。</summary>
    protected virtual void OnWaiting()
    {
    }

    /// <summary>上一段都做完了（或者还没开始）：发下一段（<see cref="Send"/>），全做完了 Complete()。</summary>
    protected abstract void Advance();

    /// <summary>这一段在等的设备都做完了没有；有设备报失败、这里发的指令被中止就失败。</summary>
    private bool Settle()
    {
        for (int i = _waiting.Count - 1; i >= 0; i--)
        {
            var (device, action, sent) = _waiting[i];
            switch (device.ActionState)
            {
                case ActionState.Completed:
                    _waiting.RemoveAt(i);
                    break;

                case ActionState.Failed:
                    Fail(ErrorCodes.ChamberDeviceActionFailed, $"{Name}：{device.FullPath} {action} 没做成", device.FullPath, action.ToString());
                    return false;

                case ActionState.Idle:
                    if (sent)
                    {
                        Fail(ErrorCodes.Aborted, $"{Name}：{device.FullPath} 被中止", Name);
                        return false;
                    }

                    _waiting.RemoveAt(i);
                    break;
            }
        }

        return _waiting.Count == 0;
    }
}
