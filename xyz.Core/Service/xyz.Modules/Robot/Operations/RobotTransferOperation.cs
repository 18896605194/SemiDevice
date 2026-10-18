using System.Diagnostics;
using System.Globalization;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 完整取放操作：设备动作成功 → 按配置确认手指持片 → 提交晶圆账，全部通过后才成功。
/// 设备、持片或记账失败均交给模块落 Error，搬运管理保留已动手的资源供人工确认，不自动重发取放动作。
/// </summary>
internal sealed class RobotTransferOperation : ModuleOperation<RobotTransferOperation.CheckStep>
{
   

    private readonly ModuleOperation _motion;
    private readonly string _robot;
    private readonly IRobotArm? _sensorArm;
    private readonly bool _expectWafer;
    private readonly Func<HandleResult?> _commitLedger;

    /// <summary>从设备做完开始计时：等传感器的时限不算设备动作的时间。</summary>
    private readonly Stopwatch _sensorWatch = new();

    public RobotTransferOperation(ModuleOperation motion, string robot, IRobotArm? sensorArm,
        bool expectWafer, Func<HandleResult?> commitLedger)
        : base(motion.Name, CheckStep.Motion)
    {
        _motion = motion;
        _robot = robot;
        _sensorArm = sensorArm;
        _expectWafer = expectWafer;
        _commitLedger = commitLedger;
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case CheckStep.Motion:
                RunMotion();
                break;

            case CheckStep.WaitSensor:
                WaitSensor();
                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }

    /// <summary>
    /// 设备动作没挂在模块上，由这里带着扫；它失败就原样带着它的错误码失败。
    /// </summary>
    private void RunMotion()
    {
        _motion.Scan();
        if (IsTerminal || !_motion.IsTerminal)
        {
            return;
        }

        if (!_motion.IsSuccess)
        {
            Fail(_motion.Code, _motion.Reason, [.. _motion.ErrorArgs]);
            return;
        }

        if (_sensorArm is null)
        {
            CommitLedger();
            return;
        }

        _sensorWatch.Restart();
        SetStep(CheckStep.WaitSensor);
        WaitSensor();
    }

    private void WaitSensor()
    {
        var sensorArm = _sensorArm!;
        string arm = sensorArm.Number.ToString(CultureInfo.InvariantCulture);
        bool? sensed = sensorArm.HasWafer;
        if (sensed == _expectWafer)
        {
            CommitLedger();
            return;
        }

        int timeout = sensorArm.WaferSensorTimeoutMs;
        if (_sensorWatch.ElapsedMilliseconds < timeout)
        {
            return;
        }

        string expected = _expectWafer ? "有片" : "没片";
        if (sensed is null)
        {
            Fail(ErrorCodes.RobotArmSensorUnknown, $"{Name} 做完 {timeout}ms 内 Arm{arm} 持片反馈读不到（该{expected}）", _robot, arm);
            return;
        }

        Fail(sensed.Value ? ErrorCodes.RobotArmSensorHasWafer : ErrorCodes.RobotArmSensorNoWafer,
            $"{Name} 做完 {timeout}ms 内 Arm{arm} 持片反馈没跟上（该{expected}）", _robot, arm);
    }

    private void CommitLedger()
    {
        if (IsTerminal)
        {
            return;
        }

        var refused = _commitLedger();
        if (refused is not null)
        {
            Fail(refused.ErrorMessage, "设备取放已完成，但晶圆账更新失败，需确认实际片位", [.. refused.Args]);
            return;
        }

        Complete();
    }

    /// <summary>
    /// 被急停、被新操作顶替：设备动作跟着打断，尚未完成的取放不提交晶圆账。
    /// </summary>
    protected override void OnAborted(string reason)
    {
        _motion.AbortByHost(reason);
    }

    internal enum CheckStep
    {
        Motion,

        WaitSensor,
    }
}
