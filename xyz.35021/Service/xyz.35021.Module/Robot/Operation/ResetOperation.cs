using xyz.Drivers.Robot.Reje.Commands;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Robot.Operation;

/// <summary>
/// Reset 操作：发送锐洁 Reset（清除控制器报错）→ 等结果帧终结；超时走模块 EC live 读。
/// </summary>
public sealed class ResetOperation : ModuleOperation<ActionStep>
{
    private readonly RobotModule _module;
    private RejeResetCommand? _command;

    public ResetOperation(RobotModule module) : base("Reset", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = new RejeResetCommand(_module.Driver!);
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
