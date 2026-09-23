using xyz.Drivers.Robot;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Robot.Operation;

/// <summary>
/// Place 操作：经驱动组件发放片（指定手指向工位槽位放片）→ 等结果帧终结；超时走模块 EC live 读。
/// </summary>
public sealed class PlaceOperation : ModuleOperation<ActionStep>
{
    private readonly RobotModule _module;
    private readonly int _arm;
    private readonly int _station;
    private readonly int _slot;
    private RobotCommand? _command;

    public PlaceOperation(RobotModule module, int arm, int station, int slot) : base("Place", ActionStep.SendCommand)
    {
        _module = module;
        _arm = arm;
        _station = station;
        _slot = slot;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = _module.Robot!.Place(_arm, _station, _slot);
                if (_command is not null)
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Place");
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
                        Fail(ErrorCodes.DeviceFailed, response.Error, "Place", response.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.PlaceTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Place 动作超时（{_module.PlaceTimeout}ms）", "Place", _module.PlaceTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
