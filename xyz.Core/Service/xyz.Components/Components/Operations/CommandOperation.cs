using System.Globalization;
using xyz.Components.Interfaces;
using xyz.Drivers.Device;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Components.Components;

/// <summary>
/// 设备动作：一段一段地走，每段发一条指令（或者等一个别家的动作、做一项检查），前一段做成才走下一段；设备回错、超时就失败
/// </summary>
public sealed class CommandOperation : ModuleOperation<CommandOperation.CommandStep>
{
    private readonly IDeviceDriverComponent? _driver;
    private readonly List<Stage> _stages = [];
    private Func<int> _timeoutMs;
    private int _stageIndex;
    private long _sentAtMs;
    private bool _hasSent;
    private DeviceCommand? _command;

    /// <summary>
    /// 正在等的别家动作（搬运管理排的机械手扫片这类）
    /// </summary>
    private ModuleOperation? _waiting;

    /// <summary>
    /// 一个动作，后面用 Then / ThenWait / ThenCheck 串；每段的超时默认用这里给的
    /// </summary>
    public CommandOperation(string name, IDeviceDriverComponent? driver, Func<int> timeoutMs) : base(name, CommandStep.SendCommand)
    {
        _driver = driver;
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// 只发一条指令的动作：发出去等它做完，超时判失败
    /// </summary>
    public CommandOperation(string name, IDeviceDriverComponent? driver, Func<DeviceCommand> create, Func<int> timeoutMs)
        : this(name, driver, timeoutMs)
    {
        Then(name, create);
    }

    /// <summary>
    /// 后面串的段改用这个超时（比如回零后接着对准，对准那几条按对准的超时算）
    /// </summary>
    public CommandOperation UseTimeout(Func<int> timeoutMs)
    {
        _timeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// 串一条指令：轮到它时新建一条发出去；做成了交给 onSuccess 看结果能不能用，不能用就失败
    /// </summary>
    public CommandOperation Then<TCommand>(string stageName, Func<TCommand> create, Func<TCommand, HandleResult>? onSuccess = null)
        where TCommand : DeviceCommand
    {
        Func<DeviceCommand, HandleResult>? check = null;
        if (onSuccess is not null)
        {
            check = command => onSuccess((TCommand)command);
        }

        _stages.Add(new Stage(stageName, () => create(), check, null, null, null, _timeoutMs));
        return this;
    }

    /// <summary>
    /// 串一段别家的动作（搬运管理排的机械手扫片）：发起被拒就失败，等它做完、失败带它的错误码；
    /// 这一段不按本动作的超时算（等机械手空出来的时间不算，它的动作有自己的超时）
    /// </summary>
    public CommandOperation ThenWait(string stageName, Func<HandleResult<ModuleOperation>> start, Func<HandleResult>? onSuccess = null)
    {
        _stages.Add(new Stage(stageName, null, null, start, onSuccess, null, _timeoutMs));
        return this;
    }

    /// <summary>
    /// 串一项检查（不发指令）：轮到它当场做，不过就失败
    /// </summary>
    public CommandOperation ThenCheck(string stageName, Func<HandleResult> check)
    {
        _stages.Add(new Stage(stageName, null, null, null, null, check, _timeoutMs));
        return this;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case CommandStep.SendCommand:
                StartStage();
                break;

            case CommandStep.WaitCommand:
                WaitStage();
                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }

    /// <summary>
    /// 被中止：排着的别家动作撤掉，在途的指令作废（同键指令以后能再发）
    /// </summary>
    protected override void OnAborted(string reason)
    {
        _waiting?.AbortByHost(reason);
        var command = _command;
        if (command is not null && !command.IsCompleted)
        {
            _driver?.Abandon(command, "Aborted");
        }
    }

    /// <summary>
    /// 开始这一段：连着的检查当场做掉；发指令、等别家动作的转去等
    /// </summary>
    private void StartStage()
    {
        if (_stages.Count == 0)
        {
            Fail(ErrorCodes.OperationFaulted, "动作里一段都没串", Name, Step.ToString());
            return;
        }

        if (!RunChecks())
        {
            return;
        }

        var current = _stages[_stageIndex];
        if (current.Start is not null)
        {
            StartWaiting(current);
            return;
        }

        SendCommand(current);
    }

    /// <summary>
    /// 当场做掉从这一段起连着的检查：有一项不过就失败，全做完了就收尾，这两种都返回 false
    /// </summary>
    private bool RunChecks()
    {
        while (_stageIndex < _stages.Count && _stages[_stageIndex].Check is not null)
        {
            var stage = _stages[_stageIndex];
            var checkedResult = stage.Check!();
            if (!checkedResult.IsSuccess)
            {
                Fail(checkedResult.ErrorMessage, $"{stage.Name} 不过：{checkedResult.ErrorMessage}", checkedResult.Args.ToArray());
                return false;
            }

            _stageIndex++;
        }

        if (_stageIndex >= _stages.Count)
        {
            Complete();
            return false;
        }

        return true;
    }

    private void StartWaiting(Stage stage)
    {
        var started = stage.Start!();
        var operation = started.Result;
        if (!started.IsSuccess || operation is null)
        {
            Fail(started.ErrorMessage, $"{stage.Name} 被拒：{started.ErrorMessage}", started.Args.ToArray());
            return;
        }

        _waiting = operation;
        SetStep(CommandStep.WaitCommand);
    }

    private void SendCommand(Stage stage)
    {
        var command = stage.Create!();
        var driver = _driver;
        if (driver is null || !driver.Send(command))
        {
            Fail(ErrorCodes.CommandRejected, $"{stage.Name} 指令被拒绝（没连上或同一条还在途）", Name);
            return;
        }

        _command = command;

        // 第一条从动作发起算超时；后面串的从它自己发出去算——Watch 是整个动作共用的表，不能把前面用掉的时间也算进去
        if (_hasSent)
        {
            _sentAtMs = Watch.ElapsedMilliseconds;
        }

        _hasSent = true;
        SetStep(CommandStep.WaitCommand);
    }

    private void WaitStage()
    {
        var stage = _stages[_stageIndex];
        if (stage.Start is not null)
        {
            WaitOperation(stage);
            return;
        }

        WaitCommand(stage);
    }

    private void WaitCommand(Stage stage)
    {
        var command = _command;
        if (command is null)
        {
            Fail(ErrorCodes.OperationFaulted, "等指令时没有指令", Name, Step.ToString());
            return;
        }

        if (command.IsCompleted)
        {
            if (!command.IsSuccess)
            {
                Fail(ErrorCodes.DeviceFailed, $"{stage.Name} 设备回错：{command.Error}", Name, command.Error);
                return;
            }

            var handled = stage.OnCommand?.Invoke(command);
            if (handled is not null && !handled.IsSuccess)
            {
                Fail(handled.ErrorMessage, $"{stage.Name} 设备做完了，但结果不能用：{handled.ErrorMessage}", handled.Args.ToArray());
                return;
            }

            NextStage();
            return;
        }

        int timeout = stage.TimeoutMs();
        if (Watch.ElapsedMilliseconds - _sentAtMs > timeout)
        {
            // 回复丢了这条就一直占着在途位：先作废让出来，复位、回零以后同一条指令才发得出去
            _driver?.Abandon(command, "Timeout");
            Fail(ErrorCodes.Timeout, $"{stage.Name} 动作超时（{timeout}ms）", Name, timeout.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// 等别家动作做完：成了接着做完成后的检查，没成原样带它的错误码
    /// </summary>
    private void WaitOperation(Stage stage)
    {
        var operation = _waiting;
        if (operation is null)
        {
            Fail(ErrorCodes.OperationFaulted, "等别的动作时没有动作", Name, Step.ToString());
            return;
        }

        if (!operation.IsTerminal)
        {
            return;
        }

        if (!operation.IsSuccess)
        {
            Fail(operation.Code, $"{stage.Name} 没成：{operation.Reason}", operation.ErrorArgs.ToArray());
            return;
        }

        var handled = stage.OnDone?.Invoke();
        if (handled is not null && !handled.IsSuccess)
        {
            Fail(handled.ErrorMessage, $"{stage.Name} 做完了，但结果不能用：{handled.ErrorMessage}", handled.Args.ToArray());
            return;
        }

        NextStage();
    }

    /// <summary>
    /// 走下一段：紧跟着的检查当场做，发指令、等别家动作的下一拍开始；全做成才算完
    /// </summary>
    private void NextStage()
    {
        _stageIndex++;
        _command = null;
        _waiting = null;
        if (!RunChecks())
        {
            return;
        }

        SetStep(CommandStep.SendCommand);
    }

    /// <summary>
    /// 动作走到哪一步：发（或开始这一段）、等
    /// </summary>
    public enum CommandStep
    {
        SendCommand,

        WaitCommand,
    }

    /// <summary>
    /// 串着的一段：发一条指令（Create，做成之后 OnCommand）、等一个别家动作（Start，做成之后 OnDone）、或者一项检查（Check）；Name 只进日志
    /// </summary>
    private sealed record Stage(string Name, Func<DeviceCommand>? Create, Func<DeviceCommand, HandleResult>? OnCommand,
        Func<HandleResult<ModuleOperation>>? Start, Func<HandleResult>? OnDone, Func<HandleResult>? Check, Func<int> TimeoutMs);
}
