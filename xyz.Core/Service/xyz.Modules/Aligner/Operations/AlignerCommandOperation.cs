using System.Globalization;
using xyz.Components.Components;
using xyz.Drivers.Aligner;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 对准器的设备动作：发一条驱动指令 → 等它做完 → 超时判失败；可以串几条，前一条做成才发下一条
/// </summary>
public sealed class AlignerCommandOperation : ModuleOperation<AlignerCommandStep>
{
    private readonly List<Stage> _stages = [];
    private int _stageIndex;
    private long _sentAtMs;
    private AlignerCommand? _command;

    public AlignerCommandOperation(string name, Func<AlignerCommand?> send, Func<int> timeoutMs)
        : base(name, AlignerCommandStep.SendCommand)
    {
        _stages.Add(new Stage(name, send, timeoutMs));
    }

    /// <summary>
    /// 串一条：前一条做成以后发，超时从它自己发出去算
    /// </summary>
    public AlignerCommandOperation Then(string stageName, Func<AlignerCommand?> send, Func<int> timeoutMs)
    {
        _stages.Add(new Stage(stageName, send, timeoutMs));
        return this;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case AlignerCommandStep.SendCommand:
                SendCommand();
                break;

            case AlignerCommandStep.WaitCommand:
                WaitCommand();
                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }

    private void SendCommand()
    {
        var stage = _stages[_stageIndex];
        _command = stage.Send();
        if (_command is null)
        {
            Fail(ErrorCodes.CommandRejected, $"{stage.Name} 指令被拒绝（未连接或在途）", Name);
            return;
        }

        // 第一条从动作发起算超时；串着的后一条从它自己发出去算，不把前一条用掉的时间算进去
        if (_stageIndex > 0)
        {
            _sentAtMs = Watch.ElapsedMilliseconds;
        }

        SetStep(AlignerCommandStep.WaitCommand);
    }

    private void WaitCommand()
    {
        var command = _command;
        if (command is null)
        {
            Fail(ErrorCodes.OperationFaulted, "等指令时没有指令", Name, Step.ToString());
            return;
        }

        var stage = _stages[_stageIndex];
        if (command.IsCompleted)
        {
            var response = command.Response;
            if (response is not null && response.IsSuccess)
            {
                if (_stageIndex + 1 < _stages.Count)
                {
                    _stageIndex++;
                    _command = null;
                    SetStep(AlignerCommandStep.SendCommand);
                    return;
                }

                Complete();
                return;
            }

            string error = string.Empty;
            if (response is not null)
            {
                error = response.Error;
            }

            Fail(ErrorCodes.DeviceFailed, $"{stage.Name} 设备报错：{error}", Name, error);
            return;
        }

        int timeout = stage.TimeoutMs();
        if (Watch.ElapsedMilliseconds - _sentAtMs > timeout)
        {
            Fail(ErrorCodes.Timeout, $"{stage.Name} 动作超时（{timeout}ms）", Name, timeout.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// 串着的一条指令：叫什么（只进日志）、怎么发、等多久
    /// </summary>
    private sealed record Stage(string Name, Func<AlignerCommand?> Send, Func<int> TimeoutMs);
}
