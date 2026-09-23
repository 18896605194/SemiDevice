using xyz.Drivers.Loadport;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Reset 操作：发设备复位清错 → 等指令终结；超时走模块 EC live 读。
/// 设备清错指令在驱动组件上叫 ResetDrive（Reset 是组件基类清报警的口，两回事）。
/// </summary>
public sealed class ResetOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private LoadPortCommand? _command;

    public ResetOperation(LoadPortModule module) : base("Reset", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = _module.Driver!.ResetDrive();
                if (_command is not null)
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Reset");
                }

                break;

            case ActionStep.WaitCommand:
                if (_command!.IsCompleted)
                {
                    var response = _command.Response!;
                    if (response.IsSuccess)
                    {
                        Complete();
                    }
                    else
                    {
                        Fail(ErrorCodes.DeviceFailed, response.Error, "Reset", response.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.ResetTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Reset 动作超时（{_module.ResetTimeout}ms）", "Reset", _module.ResetTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
