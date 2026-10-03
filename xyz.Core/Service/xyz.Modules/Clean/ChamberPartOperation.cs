using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 部件手动动作的等待：指令已经由腔体发出去了（发不出去根本不会挂这个操作），这里每拍看部件自己的动作状态——
/// 做完就成功；部件判失败（到位超时、轴报错、PLC 断了）就失败；部件被中止回到 Idle 算中止。
/// 部件都有自己的超时，EC PartActionTimeout 只是兜底，免得腔体一直停在"手动中"。
/// </summary>
internal sealed class ChamberPartOperation : ModuleOperation
{
    private readonly ComponentBase _part;
    private readonly ChamberPartAction _action;
    private readonly int _timeout;

    public ChamberPartOperation(ComponentBase part, ChamberPartAction action, int timeout)
        : base($"{part.FullPath} {action}")
    {
        _part = part;
        _action = action;
        _timeout = timeout;
    }

    protected override void OnScan()
    {
        switch (ActionStateOf(_part))
        {
            case ActionState.Completed:
                Complete();
                return;

            case ActionState.Failed:
                Fail(ErrorCodes.ChamberPartActionFailed, $"{Name}：部件报失败（到位超时或设备报错）",
                    _part.FullPath, _action.ToString());
                return;

            case ActionState.Idle:
                Fail(ErrorCodes.Aborted, $"{Name}：部件被中止", Name);
                return;
        }

        if (Watch.ElapsedMilliseconds > _timeout)
        {
            Fail(ErrorCodes.ChamberPartActionFailed, $"{Name}：等了 {_timeout}ms 还没做完",
                _part.FullPath, _action.ToString());
        }
    }

    /// <summary>部件当前动作的状态：气缸、阀、轴各自管自己的到位和超时。</summary>
    private static ActionState ActionStateOf(ComponentBase part)
    {
        switch (part)
        {
            case TwoStateComponent cylinder:
                return cylinder.ActionState;

            case OneStateComponent valve:
                return valve.ActionState;

            case AxisComponent axis:
                return axis.ActionState;

            default:
                return ActionState.Failed;
        }
    }
}
