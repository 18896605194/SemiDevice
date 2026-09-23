using xyz.Drivers.Robot;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Robot.Operation;

/// <summary>
/// Abort 操作：经驱动组件发急停 → 等结果帧终结；超时走模块 EC live 读。
/// 被打断的运动指令设备不再回结果，由驱动在急停确认后以失败终结。
/// </summary>
public sealed class AbortOperation : ModuleOperation<ActionStep>
{
    private readonly RobotModule _module;
    private RobotCommand? _command;

    public AbortOperation(RobotModule module) : base("Abort", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = _module.Robot!.Stop();
                if (_command is not null)
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Abort");
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
                        Fail(ErrorCodes.DeviceFailed, response.Error, "Abort", response.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.AbortTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Abort 动作超时（{_module.AbortTimeout}ms）", "Abort", _module.AbortTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
