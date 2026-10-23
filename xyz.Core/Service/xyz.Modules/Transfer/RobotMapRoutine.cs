using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 机械手替 LoadPort 扫一次片：在搬运管理里排队，轮到了、机械手没在搬片、手上全空才叫它扫；搬运管理的扫描线程推进。
/// 被撤时机械手正在扫的不打断，让它扫完、结果不用
/// </summary>
public sealed class RobotMapRoutine : ModuleRoutine
{
    private readonly TransferManager _manager;

    /// <summary>
    /// 等机械手的日志只记一次
    /// </summary>
    private bool _waitLogged;

    /// <summary>
    /// 排上一条扫片：module 是日志记在谁名下（搬运管理）
    /// </summary>
    public RobotMapRoutine(string module, TransferManager manager, ITransferRobot robot, string station) : base(module, "Map")
    {
        _manager = manager;
        Robot = robot;
        Station = station;
        Add($"等 {robot.Name} 空出来", null, WaitRobot);
        Add($"{robot.Name} 扫 {station}", null, WaitMap);
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
    public ModuleRoutine? Motion { get; private set; }

    /// <summary>
    /// 可以从队列里拿掉了：自己停下了，机械手的扫片动作也停下了（被撤时机械手正在扫，等它扫完再放开机械手）
    /// </summary>
    public bool ReadyToFinish
    {
        get
        {
            var motion = Motion;
            return IsTerminal && (motion is null || motion.IsTerminal);
        }
    }

    /// <summary>
    /// 排队等机械手：停用、维护模式、出错或没初始化不等，直接判失败；轮到了叫它扫，它在忙就下一拍再试
    /// </summary>
    private bool WaitRobot(RoutineStep step)
    {
        if (!Robot.IsEnabled)
        {
            step.Fail(ErrorCodes.ModuleDisabled, $"{Robot.Name} 停用了，扫不了 {Station}", Robot.Name);
            return false;
        }

        if (Robot is IMode mode && mode.Mode != ModuleMode.Production)
        {
            step.Fail(ErrorCodes.ModuleMaintenance, $"{Robot.Name} 在维护模式，扫不了 {Station}", Robot.Name);
            return false;
        }

        int state = Robot.State;
        if (state == ModuleState.Error || state == ModuleState.NotInit)
        {
            step.Fail(ErrorCodes.ModuleStateNotAllowed, $"{Robot.Name} 出错或没初始化（{state}），扫不了 {Station}",
                Robot.Name, state.ToString(CultureInfo.InvariantCulture));
            return false;
        }

        if (!_manager.IsMapTurn(this))
        {
            if (!_waitLogged)
            {
                _waitLogged = true;
                LogHelper.Info(Robot.Name, $"{Station} 要扫片：等 {Robot.Name} 搬完、手上的片都放下");
            }

            return false;
        }

        if (Robot is not IWaferMap mapper)
        {
            step.Fail(ErrorCodes.RobotMapUnsupported, $"{Robot.Name} 不能扫片", Robot.Name);
            return false;
        }

        var started = mapper.Map(Station);
        var motion = started.Result;
        if (started.IsSuccess && motion is not null)
        {
            Motion = motion;
            return true;
        }

        // 机械手刚好在做别的（手动页回原点这类）：下一拍再试
        if (started.ErrorMessage == ErrorCodes.ModuleBusy || started.ErrorMessage == ErrorCodes.ModuleStateNotAllowed
            || started.ErrorMessage == ErrorCodes.ActionRejected)
        {
            return false;
        }

        step.Fail(started.ErrorMessage, $"{Robot.Name} 扫片被拒：{started.ErrorMessage}", [.. started.Args]);
        return false;
    }

    /// <summary>
    /// 机械手在扫：扫完算做完，没扫成原样带它的错误码
    /// </summary>
    private bool WaitMap(RoutineStep step)
    {
        var motion = Motion;
        if (motion is null || !motion.IsTerminal)
        {
            return false;
        }

        if (motion.IsSuccess)
        {
            return true;
        }

        step.Fail(motion.Code, $"{Robot.Name} 扫 {Station} 没成：{motion.Reason}", [.. motion.ErrorArgs]);
        return false;
    }
}
