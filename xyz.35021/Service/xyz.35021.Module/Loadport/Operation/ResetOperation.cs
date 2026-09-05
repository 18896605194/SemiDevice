using xyz.Drivers.Loadport.FCD.Commands;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Reset 操作：发送 FCD SET:RESET（复位清错）→ 等 INF 终结；超时走模块 EC live 读。
/// </summary>
public sealed class ResetOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private FcdResetCommand? _command;

    public ResetOperation(LoadPortModule module) : base("Reset", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = new FcdResetCommand(_module.Driver!);
                if (_command.Execute())
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
                    if (_command.IsSucceeded)
                    {
                        Complete();
                    }
                    else
                    {
                        Fail(ErrorCodes.DeviceFailed, _command.Error, "Reset", _command.Error);
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
