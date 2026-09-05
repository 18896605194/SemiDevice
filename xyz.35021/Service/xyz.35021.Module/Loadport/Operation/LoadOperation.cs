using xyz.Drivers.Loadport.FCD.Commands;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Load 操作：发送 FCD CLOAD（开门+Mapping）→ 等 INF 终结；超时走模块 EC live 读；成功收 SlotMap。
/// </summary>
public sealed class LoadOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private FcdLoadCommand? _command;

    public LoadOperation(LoadPortModule module) : base("Load", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = new FcdLoadCommand(_module.Driver!);
                if (_command.Execute())
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Load");
                }

                break;

            case ActionStep.WaitCommand:
                if (_command!.IsCompleted)
                {
                    if (_command.IsSucceeded)
                    {
                        _module.SlotMap = _command.SlotMap;
                        Complete();
                    }
                    else
                    {
                        Fail(ErrorCodes.DeviceFailed, _command.Error, "Load", _command.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.LoadTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Load 动作超时（{_module.LoadTimeout}ms）", "Load", _module.LoadTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
