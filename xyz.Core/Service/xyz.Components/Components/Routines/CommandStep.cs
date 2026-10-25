using System.Globalization;
using xyz.Components.Interfaces;
using xyz.Drivers.Device;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Components.Components;

/// <summary>
/// 一步：发一条驱动指令、等设备回。设备回错、结果不能用、超时（先作废这一条）就失败；动作被中止时作废在途的这一条
/// </summary>
internal sealed class CommandStep : RoutineStep
{
    private readonly ICommDriver? _driver;
    private readonly Func<DeviceCommand> _create;
    private readonly Func<int> _timeoutMs;
    private readonly Func<DeviceCommand, HandleResult>? _onSuccess;

    /// <summary>
    /// 发出去的这一条；还没发为 null
    /// </summary>
    private DeviceCommand? _command;

    public CommandStep(string name, ICommDriver? driver, Func<DeviceCommand> create, Func<int> timeoutMs,
        Func<DeviceCommand, HandleResult>? onSuccess) : base(name)
    {
        _driver = driver;
        _create = create;
        _timeoutMs = timeoutMs;
        _onSuccess = onSuccess;
    }

    /// <summary>
    /// 在等设备回哪一条
    /// </summary>
    public override string Waiting
    {
        get
        {
            var command = _command;
            if (command is null || command.IsCompleted)
            {
                return string.Empty;
            }

            return $"设备回 {command.Key}";
        }
    }

    protected internal override void Run()
    {
        var command = _create();
        var driver = _driver;
        if (driver is null || !driver.Send(command))
        {
            Fail(ErrorCodes.CommandRejected, "指令被拒绝（没连上或同一条还在途）", Routine.Name);
            return;
        }

        _command = command;
    }

    protected internal override bool Check()
    {
        var command = _command;
        if (command is null)
        {
            return false;
        }

        if (command.IsCompleted)
        {
            if (!command.IsSuccess)
            {
                Fail(ErrorCodes.DeviceFailed, $"设备回错：{command.Error}", Routine.Name, command.Error);
                return false;
            }

            var check = _onSuccess;
            if (check is not null)
            {
                var handled = check(command);
                if (!handled.IsSuccess)
                {
                    Fail(handled.ErrorMessage, $"设备做完了，但结果不能用：{handled.ErrorMessage}", handled.Args.ToArray());
                    return false;
                }
            }

            return true;
        }

        int timeout = _timeoutMs();
        if (ElapsedMs > timeout)
        {
            // 回复丢了这条就一直占着在途位：先作废让出来，复位、回零以后同一条指令才发得出去
            _driver?.Abandon(command, "Timeout");
            Fail(ErrorCodes.Timeout, $"{timeout} ms 设备没回", Routine.Name, timeout.ToString(CultureInfo.InvariantCulture));
        }

        return false;
    }

    protected internal override void OnAborted(string reason)
    {
        var command = _command;
        if (command is not null && !command.IsCompleted)
        {
            _driver?.Abandon(command, "Aborted");
        }
    }
}
