using xyz.Drivers.Loadport.FCD.Commands;
using xyz.Modules;
using xyz.Shared.Errors;

namespace xyz._35021.Module.Loadport.Operation;

/// <summary>
/// Home 操作：发送 FCD ORGSH（整机回零）→ 等 INF 终结；超时走模块 EC live 读。
/// </summary>
public sealed class HomeOperation : ModuleOperation<ActionStep>
{
    private readonly LoadPortModule _module;
    private FcdHomeCommand? _command;

    public HomeOperation(LoadPortModule module) : base("Home", ActionStep.SendCommand)
    {
        _module = module;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case ActionStep.SendCommand:
                _command = new FcdHomeCommand(_module.Driver!);
                if (_command.Execute())
                {
                    SetStep(ActionStep.WaitCommand);
                }
                else
                {
                    Fail(ErrorCodes.CommandRejected, "指令被拒绝（未连接或在途）", "Home");
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
                        Fail(ErrorCodes.DeviceFailed, _command.Error, "Home", _command.Error);
                    }
                }
                else if (Watch.ElapsedMilliseconds > _module.HomeTimeout)
                {
                    Fail(ErrorCodes.Timeout, $"Home 动作超时（{_module.HomeTimeout}ms）", "Home", _module.HomeTimeout.ToString());
                }

                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }
}
