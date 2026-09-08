using xyz.Drivers.Loadport.FCD.Commands;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// E84 上线操作：发送 FCD SET:E84EN/01 → 等 INF 终结；超时走模块 EC live 读。
/// </summary>
public sealed class OnlineOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private FcdOnlineCommand? _command;

    public OnlineOperation(LoadPortModule module) : base("Online", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = new FcdOnlineCommand(_module.Driver!);
                if (_command.Execute())
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Online");
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
                        Fail(ErrorCodes.DeviceFailed, _command.Error, "Online", _command.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.OnlineTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Online 动作超时（{_module.OnlineTimeout}ms）", "Online", _module.OnlineTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
