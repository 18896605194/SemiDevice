using xyz.Drivers.Robot.Reje.Commands;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Robot.Operation;

/// <summary>
/// Pick 操作：发送锐洁 G（指定手指从工位槽位取片）→ 等结果帧终结；超时走模块 EC live 读。
/// </summary>
public sealed class PickOperation : ModuleOperation<ActionStep>
{
    private readonly RobotModule _module;
    private readonly int _arm;
    private readonly int _station;
    private readonly int _slot;
    private RejePickCommand? _command;

    public PickOperation(RobotModule module, int arm, int station, int slot) : base("Pick", ActionStep.SendCommand)
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
                _command = new RejePickCommand(_module.Driver!, _arm, _station, _slot);
                if (_command.Execute())
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Pick");
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
                        Fail(ErrorCodes.DeviceFailed, response.Error, "Pick", response.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.PickTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Pick 动作超时（{_module.PickTimeout}ms）", "Pick", _module.PickTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
