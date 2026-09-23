using xyz.Drivers.Loadport;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Abort 操作：发设备急停 → 等指令终结；超时走模块 EC live 读。
/// 急停指令在驱动组件上叫 Stop（Abort 是组件基类中止上层操作的口，两回事）。
/// </summary>
public sealed class AbortOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private LoadPortCommand? _command;

    public AbortOperation(LoadPortModule module) : base("Abort", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = _module.Driver!.Stop();
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
