using xyz.Drivers.Loadport;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Clamp 操作：夹紧 FOUP → 等指令终结；超时走模块 EC live 读。
/// </summary>
public sealed class ClampOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private LoadPortCommand? _command;

    public ClampOperation(LoadPortModule module) : base("Clamp", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = _module.Driver!.Clamp();
                if (_command is not null)
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Clamp");
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
                        Fail(ErrorCodes.DeviceFailed, response.Error, "Clamp", response.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.ClampTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Clamp 动作超时（{_module.ClampTimeout}ms）", "Clamp", _module.ClampTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
