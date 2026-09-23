using xyz.Drivers.Robot;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Robot.Operation;

/// <summary>
/// Home 操作：经驱动组件发回原点（全轴回原点）→ 等结果帧终结；超时走模块 EC live 读。
/// </summary>
public sealed class HomeOperation : ModuleOperation<ActionStep>
{
    private readonly RobotModule _module;
    private RobotCommand? _command;

    public HomeOperation(RobotModule module) : base("Home", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = _module.Robot!.Home();
                if (_command is not null)
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Home");
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
                        Fail(ErrorCodes.DeviceFailed, response.Error, "Home", response.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.HomeTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Home 动作超时（{_module.HomeTimeout}ms）", "Home", _module.HomeTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
