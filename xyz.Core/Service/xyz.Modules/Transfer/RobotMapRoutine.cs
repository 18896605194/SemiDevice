using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 机械手替 LoadPort 扫一次片：在搬运管理里排队，轮到了、机械手没在搬片、手上全空才叫它扫；搬运管理的扫描线程推进
/// </summary>
public sealed class RobotMapRoutine : ModuleOperation<RobotMapStep>
{
    private readonly TransferManager _manager;

    /// <summary>
    /// 等机械手的日志只记一次
    /// </summary>
    private bool _waitLogged;

    public RobotMapRoutine(TransferManager manager, ITransferRobot robot, string station) : base("Map", RobotMapStep.WaitRobot)
    {
        _manager = manager;
        Robot = robot;
        Station = station;
    }

    /// <summary>
    /// 扫片的机械手
    /// </summary>
    public ITransferRobot Robot { get; }

    /// <summary>
    /// 要扫的站点（LoadPort 名）
    /// </summary>
    public string Station { get; }

    /// <summary>
    /// 机械手的扫片动作；还没轮到是 null
    /// </summary>
    public ModuleOperation? Motion { get; private set; }

    /// <summary>
    /// 可以从队列里拿掉了：自己做完了，机械手的扫片动作也停下了（被中止时机械手正在扫，等它扫完再放开机械手）
    /// </summary>
    public bool ReadyToFinish
    {
        get
        {
            var motion = Motion;
            return IsTerminal && (motion is null || motion.IsTerminal);
        }
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case RobotMapStep.WaitRobot:
                WaitRobot();
                break;

            case RobotMapStep.WaitMap:
                WaitMap();
                break;

            default:
                Fail(ErrorCodes.OperationFaulted, $"未知步骤: {Step}", Name, Step.ToString());
                break;
        }
    }

    /// <summary>
    /// 机械手停用、维护模式、出错或没初始化：不等，直接判失败；在忙就接着等
    /// </summary>
    private void WaitRobot()
    {
        if (!Robot.IsEnabled)
        {
            Fail(ErrorCodes.ModuleDisabled, $"{Robot.Name} 停用了，扫不了 {Station}", Robot.Name);
            return;
        }

        if (Robot is IMode mode && mode.Mode != ModuleMode.Production)
        {
            Fail(ErrorCodes.ModuleMaintenance, $"{Robot.Name} 在维护模式，扫不了 {Station}", Robot.Name);
            return;
        }

        int state = Robot.State;
        if (state == ModuleState.Error || state == ModuleState.NotInit)
        {
            Fail(ErrorCodes.ModuleStateNotAllowed, $"{Robot.Name} 出错或没初始化（{state}），扫不了 {Station}",
                Robot.Name, state.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (!_manager.IsMapTurn(this))
        {
            if (!_waitLogged)
            {
                _waitLogged = true;
                LogHelper.Info(Robot.Name, $"{Station} 要扫片：等 {Robot.Name} 搬完、手上的片都放下");
            }

            return;
        }

        if (Robot is not IWaferMap mapper)
        {
            Fail(ErrorCodes.RobotMapUnsupported, $"{Robot.Name} 不能扫片", Robot.Name);
            return;
        }

        var started = mapper.Map(Station);
        var motion = started.Result;
        if (started.IsSuccess && motion is not null)
        {
            Motion = motion;
            SetStep(RobotMapStep.WaitMap);
            return;
        }

        // 机械手刚好在做别的（手动页回原点这类）：下一拍再试
        if (started.ErrorMessage == ErrorCodes.ModuleBusy || started.ErrorMessage == ErrorCodes.ModuleStateNotAllowed
            || started.ErrorMessage == ErrorCodes.ActionRejected)
        {
            return;
        }

        Fail(started.ErrorMessage, $"{Robot.Name} 扫片被拒：{started.ErrorMessage}", started.Args.ToArray());
    }

    private void WaitMap()
    {
        var motion = Motion;
        if (motion is null || !motion.IsTerminal)
        {
            return;
        }

        if (motion.IsSuccess)
        {
            Complete();
            return;
        }

        Fail(motion.Code, $"{Robot.Name} 扫 {Station} 没成：{motion.Reason}", motion.ErrorArgs.ToArray());
    }
}
