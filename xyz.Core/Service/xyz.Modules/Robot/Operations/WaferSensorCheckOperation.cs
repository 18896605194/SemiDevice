using System.Diagnostics;
using System.Globalization;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 取放片做完再对一次手指在位传感器：设备动作成功以后，这只手的传感器要在手指的 EC WaferSensorTimeoutMs 内变成该有的样子
/// （取完有片、放完没片）才算做成，模块才记账；时限内对不上判失败、不记账——片可能还在站点上或挂在手上，
/// 搬运管理照"动过手才失败"留着资源、站点停在交互中，等人到现场确认片位。只套在挂了在位传感器的手上。
/// </summary>
internal sealed class WaferSensorCheckOperation : ModuleOperation<WaferSensorCheckOperation.CheckStep>
{
    internal enum CheckStep
    {
        /// <summary>设备动作（取 / 放片指令）还在做。</summary>
        Motion,

        /// <summary>设备做完了，等传感器跟上。</summary>
        WaitSensor,
    }

    private readonly ModuleOperation _motion;
    private readonly string _robot;
    private readonly IRobotArm _arm;
    private readonly bool _expectWafer;

    /// <summary>从设备做完开始计时：等传感器的时限不算设备动作的时间。</summary>
    private readonly Stopwatch _sensorWatch = new();

    public WaferSensorCheckOperation(ModuleOperation motion, string robot, IRobotArm arm, bool expectWafer)
        : base(motion.Name, CheckStep.Motion)
    {
        _motion = motion;
        _robot = robot;
        _arm = arm;
        _expectWafer = expectWafer;
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
        if (!_motion.IsTerminal)
        {
            return;
        }

        if (!_motion.IsSuccess)
        {
            Fail(_motion.Code, _motion.Reason, [.. _motion.ErrorArgs]);
            return;
        }

        _sensorWatch.Restart();
        SetStep(CheckStep.WaitSensor);
        WaitSensor();
    }

    private void WaitSensor()
    {
        string arm = _arm.Number.ToString(CultureInfo.InvariantCulture);
        bool? sensed = _arm.HasWafer;
        if (sensed == _expectWafer)
        {
            Complete();
            return;
        }

        int timeout = _arm.WaferSensorTimeoutMs;
        if (_sensorWatch.ElapsedMilliseconds < timeout)
        {
            return;
        }

        string expected = _expectWafer ? "有片" : "没片";
        if (sensed is null)
        {
            Fail(ErrorCodes.RobotArmSensorUnknown, $"{Name} 做完 {timeout}ms 内 Arm{arm} 在位传感器读不到（该{expected}）", _robot, arm);
            return;
        }

        Fail(sensed.Value ? ErrorCodes.RobotArmSensorHasWafer : ErrorCodes.RobotArmSensorNoWafer,
            $"{Name} 做完 {timeout}ms 内 Arm{arm} 在位传感器没跟上（该{expected}）", _robot, arm);
    }

    /// <summary>
    /// 被急停、被新操作顶替：设备动作跟着打断（它自己的收尾照旧）。
    /// </summary>
    protected override void OnAborted(string reason)
    {
        _motion.AbortByHost(reason);
    }
}
