using xyz.Drivers.Robot;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Robot.Operation;

/// <summary>
/// PowerOn 操作：经驱动组件发伺服上使能 → 等结果帧终结；超时走模块 EC live 读。
/// </summary>
public sealed class PowerOnOperation : ModuleOperation<ActionStep>
{
    private readonly RobotModule _module;
    private RobotCommand? _command;

    public PowerOnOperation(RobotModule module) : base("PowerOn", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = _module.Robot!.PowerOn();
                if (_command is not null)
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "PowerOn");
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
                        Fail(ErrorCodes.DeviceFailed, response.Error, "PowerOn", response.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.PowerTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"PowerOn 动作超时（{_module.PowerTimeout}ms）", "PowerOn", _module.PowerTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
