using xyz.Components.Components;
using xyz.Drivers.Robot;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 机械手的一条设备指令：经驱动组件发出去 → 等结果帧 → 设备回成功就成功、回错就失败，超过 EC 超时判负。
/// 平台各动作（Home、Pick、Place、上下使能、复位、急停）默认都用它，机型的动作不一样才在 CreateXxxOperation 里换掉。
/// </summary>
public sealed class RobotCommandOperation : ModuleOperation<RobotCommandStep>
{
    private readonly Func<RobotCommand?> _send;
    private readonly Func<int> _timeoutMs;
    private RobotCommand? _command;

    public RobotCommandOperation(string name, Func<RobotCommand?> send, Func<int> timeoutMs) : base(name, RobotCommandStep.SendCommand)
    {
        _send = send;
        _timeoutMs = timeoutMs;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case RobotCommandStep.SendCommand:
                SendCommand();
                break;

            case RobotCommandStep.WaitCommand:
                WaitCommand();
                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }

    private void SendCommand()
    {
        _command = _send();
        if (_command is null)
        {
            Fail(ErrorCodes.CommandRejected, $"{Name} 指令被拒绝（未连接或在途）", Name);
            return;
        }

        SetStep(RobotCommandStep.WaitCommand);
    }

    private void WaitCommand()
    {
        var command = _command;
        if (command is null)
        {
            Fail(ErrorCodes.OperationFaulted, "等指令时没有指令", Name, Step.ToString());
            return;
        }

        if (command.IsCompleted)
        {
            var response = command.Response;
            if (response is not null && response.IsSuccess)
            {
                Complete();
                return;
            }

            string error = response?.Error ?? string.Empty;
            Fail(ErrorCodes.DeviceFailed, error, Name, error);
            return;
        }

        int timeout = _timeoutMs();
        if (Watch.ElapsedMilliseconds > timeout)
        {
            // 回复丢了这条就一直占着在途位：先作废让出来，复位、回零以后同一条指令（比如再取同一槽）才发得出去
            command.AbandonInFlight("Timeout");
            Fail(ErrorCodes.Timeout, $"{Name} 动作超时（{timeout}ms）", Name, timeout.ToString());
        }
    }
}
