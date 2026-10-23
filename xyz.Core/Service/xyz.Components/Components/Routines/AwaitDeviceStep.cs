using System.Globalization;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Components.Components;

/// <summary>
/// 一步：只等设备做完，指令是别处发的（复位、中止时组件自己发的，或手动页入口刚发的）
/// </summary>
internal sealed class AwaitDeviceStep : RoutineStep
{
    private readonly IReadOnlyList<IDevice> _devices;
    private readonly ChamberDeviceAction _action;

    /// <summary>
    /// 指令是入口刚发的：回到 Idle 算被中止
    /// </summary>
    private readonly bool _sent;

    /// <summary>
    /// 等多久判没做成；为 null 不限（整个动作的上限照样管）
    /// </summary>
    private readonly Func<int>? _timeoutMs;

    public AwaitDeviceStep(string name, IReadOnlyList<IDevice> devices, ChamberDeviceAction action, bool sent, Func<int>? timeoutMs)
        : base(name)
    {
        _devices = devices;
        _action = action;
        _sent = sent;
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// 还没做完的设备
    /// </summary>
    public override string Waiting
    {
        get
        {
            var running = new List<string>();
            foreach (var device in _devices)
            {
                if (device.ActionState == ActionState.Running)
                {
                    running.Add(device.FullPath);
                }
            }

            return string.Join("、", running);
        }
    }

    protected internal override void Run()
    {
    }

    protected internal override bool Check()
    {
        bool allDone = true;
        IDevice? pending = null;
        foreach (var device in _devices)
        {
            switch (device.ActionState)
            {
                case ActionState.Failed:
                    Fail(ErrorCodes.ChamberDeviceActionFailed, $"{device.FullPath} {_action} 没做成", device.FullPath, _action.ToString());
                    return false;

                case ActionState.Idle:
                    if (_sent)
                    {
                        Fail(ErrorCodes.Aborted, $"{device.FullPath} 被中止", Routine.Name);
                        return false;
                    }

                    break;

                case ActionState.Running:
                    allDone = false;
                    if (pending is null)
                    {
                        pending = device;
                    }

                    break;

                default:
                    break;
            }
        }

        if (allDone)
        {
            return true;
        }

        var timeout = _timeoutMs;
        if (timeout is not null && pending is not null)
        {
            int timeoutMs = timeout();
            if (ElapsedMs > timeoutMs)
            {
                Fail(ErrorCodes.ChamberDeviceActionFailed, $"等了 {timeoutMs.ToString(CultureInfo.InvariantCulture)} ms 还没做完",
                    pending.FullPath, _action.ToString());
            }
        }

        return false;
    }
}
