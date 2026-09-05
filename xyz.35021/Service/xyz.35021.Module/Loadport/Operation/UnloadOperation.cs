using xyz.Drivers.Loadport.FCD.Commands;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Unload 操作：发送 FCD CULOD（关门）→ 等 INF 终结；超时走模块 EC live 读。
/// </summary>
public sealed class UnloadOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private FcdUnloadCommand? _command;

    public UnloadOperation(LoadPortModule module) : base("Unload", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = new FcdUnloadCommand(_module.Driver!);
                if (_command.Execute())
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Unload");
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
                        Fail(ErrorCodes.DeviceFailed, _command.Error, "Unload", _command.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.UnloadTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Unload 动作超时（{_module.UnloadTimeout}ms）", "Unload", _module.UnloadTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
