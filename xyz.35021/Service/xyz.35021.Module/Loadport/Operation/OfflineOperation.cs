using xyz.Drivers.Loadport.FCD.Commands;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// E84 下线操作：发送 FCD SET:E84EN/00 → 等 INF 终结；超时走模块 EC live 读。
/// </summary>
public sealed class OfflineOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private FcdOfflineCommand? _command;

    public OfflineOperation(LoadPortModule module) : base("Offline", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = new FcdOfflineCommand(_module.Driver!);
                if (_command.Execute())
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Offline");
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
                        Fail(ErrorCodes.DeviceFailed, _command.Error, "Offline", _command.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.OfflineTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Offline 动作超时（{_module.OfflineTimeout}ms）", "Offline", _module.OfflineTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
