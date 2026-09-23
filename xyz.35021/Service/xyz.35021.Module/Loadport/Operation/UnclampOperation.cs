using xyz.Drivers.Loadport;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Unclamp 操作：松开 FOUP → 等指令终结；超时走模块 EC live 读。
/// </summary>
public sealed class UnclampOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private LoadPortCommand? _command;

    public UnclampOperation(LoadPortModule module) : base("Unclamp", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = _module.Driver!.Unclamp();
                if (_command is not null)
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Unclamp");
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
                        Fail(ErrorCodes.DeviceFailed, response.Error, "Unclamp", response.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.UnclampTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Unclamp 动作超时（{_module.UnclampTimeout}ms）", "Unclamp", _module.UnclampTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
