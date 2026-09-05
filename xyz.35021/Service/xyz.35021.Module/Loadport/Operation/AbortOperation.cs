using xyz.Drivers.Loadport.FCD.Commands;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Abort 操作：发送 FCD ABORT（终止）→ 等 INF 终结；超时走模块 EC live 读。
/// </summary>
public sealed class AbortOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private FcdAbortCommand? _command;

    public AbortOperation(LoadPortModule module) : base("Abort", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = new FcdAbortCommand(_module.Driver!);
                if (_command.Execute())
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
                    if (_command.IsSucceeded)
                    {
                        Complete();
                    }
                    else
                    {
                        Fail(ErrorCodes.DeviceFailed, _command.Error, "Abort", _command.Error);
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
