using xyz.Drivers.Loadport;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Unload 操作：关门 → 等指令终结；超时走模块 EC live 读。
/// </summary>
public sealed class UnloadOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private LoadPortCommand? _command;

    public UnloadOperation(LoadPortModule module) : base("Unload", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = _module.Driver!.Unload();
                if (_command is not null)
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
                    var response = _command.Response!;
                    if (response.IsSuccess)
                    {
                        Complete();
                    }
                    else
                    {
                        Fail(ErrorCodes.DeviceFailed, response.Error, "Unload", response.Error);
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
