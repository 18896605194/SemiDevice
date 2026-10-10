using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 手动页上一个设备动作的等待：指令已经由腔体发出去了（发不出去根本不会挂这个操作），这里每拍看设备自己说的动作状态（ComponentBase.ActionState）——
/// 做完就成功；设备判失败（到位超时、轴报错、PLC 断了）就失败；设备被中止回到 Idle 算中止。
/// 设备都有自己的超时，EC DeviceActionTimeout 只是兜底，免得腔体一直停在"手动中"。
/// </summary>
internal sealed class ChamberDeviceOperation : ModuleOperation
{
    private readonly ComponentBase _part;
    private readonly ChamberDeviceAction _action;
    private readonly int _timeout;

    public ChamberDeviceOperation(ComponentBase part, ChamberDeviceAction action, int timeout)
        : base($"{part.FullPath} {action}")
    {
        _part = part;
        _action = action;
        _timeout = timeout;
    }

    protected override void OnScan()
    {
        switch (_part.ActionState)
        {
            case ActionState.Completed:
                Complete();
                return;

            case ActionState.Failed:
                Fail(ErrorCodes.ChamberDeviceActionFailed, $"{Name}：设备报失败（到位超时或设备报错）", _part.FullPath, _action.ToString());
                return;

            case ActionState.Idle:
                Fail(ErrorCodes.Aborted, $"{Name}：设备被中止", Name);
                return;
        }

        if (Watch.ElapsedMilliseconds > _timeout)
        {
            Fail(ErrorCodes.ChamberDeviceActionFailed, $"{Name}：等了 {_timeout}ms 还没做完", _part.FullPath, _action.ToString());
        }
    }
}
