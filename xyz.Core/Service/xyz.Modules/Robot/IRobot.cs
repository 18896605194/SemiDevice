using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 完整机械手模块：继承公共搬运能力，并提供回原点、伺服控制、诊断及服务接口。
/// </summary>
public interface IRobot : ITransferRobot
{
    #region 状态

    /// <summary>
    /// 是否上使能
    /// </summary>
    bool? IsServoOn { get; }

    /// <summary>
    /// 当前报错（错误码#内容）
    /// </summary>
    string? DeviceError { get; }

    #endregion

    #region 动作

    // 动作入口按 状态 → 资源 → 互锁 查，被拒带原因（错误码 + 参数）；复位、急停跟着组件基类，被拒返回 null。

    HandleResult<ModuleOperation> Home();

    HandleResult<ModuleOperation> PowerOn();

    HandleResult<ModuleOperation> PowerOff();

    #endregion

    #region 给服务用的（手动页、主界面）

    /// <summary>
    /// 回原点等到做完的上限（ms）
    /// </summary>
    int HomeTimeout { get; }

    /// <summary>
    /// 复位等到做完的上限（ms）
    /// </summary>
    int ResetTimeout { get; }

    /// <summary>
    /// 中止等到做完的上限（ms）
    /// </summary>
    int AbortTimeout { get; }

    /// <summary>
    /// 上电、断电等到做完的上限（ms）
    /// </summary>
    int PowerTimeout { get; }

    /// <summary>
    /// 复位能不能做，能是 null
    /// </summary>
    HandleResult? CheckReset();

    /// <summary>
    /// 中止能不能做，能是 null
    /// </summary>
    HandleResult? CheckAbort();

    /// <summary>
    /// 现在的状态（推给界面的那一份）
    /// </summary>
    RobotDto CreateStateDto();

    #endregion
}
