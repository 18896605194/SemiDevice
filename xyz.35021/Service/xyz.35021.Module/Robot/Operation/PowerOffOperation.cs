using xyz.Drivers.Robot;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Robot.Operation;

/// <summary>
/// PowerOff 操作：经驱动组件发伺服下使能 → 等结果帧终结；超时走模块 EC live 读。
/// </summary>
public sealed class PowerOffOperation : ModuleOperation<ActionStep>
{
    private readonly RobotModule _module;
    private RobotCommand? _command;

    public PowerOffOperation(RobotModule module) : base("PowerOff", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = _module.Robot!.PowerOff();
                if (_command is not null)
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "PowerOff");
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
                        Fail(ErrorCodes.DeviceFailed, response.Error, "PowerOff", response.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.PowerTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"PowerOff 动作超时（{_module.PowerTimeout}ms）", "PowerOff", _module.PowerTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
