using xyz.Drivers.Loadport.FCD.Commands;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Unclamp 操作：发送 FCD PODOP（松开 FOUP）→ 等 INF 终结；超时走模块 EC live 读。
/// </summary>
public sealed class UnclampOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private FcdUnclampCommand? _command;

    public UnclampOperation(LoadPortModule module) : base("Unclamp", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = new FcdUnclampCommand(_module.Driver!);
                if (_command.Execute())
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
                    if (_command.IsSucceeded)
                    {
                        Complete();
                    }
                    else
                    {
                        Fail(ErrorCodes.DeviceFailed, _command.Error, "Unclamp", _command.Error);
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
