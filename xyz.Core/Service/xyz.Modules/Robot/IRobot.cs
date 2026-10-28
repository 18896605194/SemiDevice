using xyz.Elements.Elements;
using xyz.Elements.Interfaces;
using xyz.Shared.Dtos;
using xyz.Elements.Attributes;

namespace xyz.Modules;

/// <summary>
/// 完整机械手模块：继承公共搬运能力，并提供回原点、伺服控制、诊断及服务接口。
/// </summary>
[Ioc]
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

    // 动作入口按 状态 → 资源 → 互锁 查，被拒带原因（错误码 + 参数）；复位、急停跟着元件基类，被拒返回 null。

    HandleResult<ModuleRoutine> Home();

    HandleResult<ModuleRoutine> PowerOn();

    HandleResult<ModuleRoutine> PowerOff();

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
    /// 手指的在位传感器跟晶圆账对一遍：machine.xml 配了传感器的手才比，对得上或都没配回 null
    /// </summary>
    HandleResult? CheckWaferSensors();

    /// <summary>
    /// 现在的状态（推给界面的那一份）
    /// </summary>
    RobotDto CreateStateDto();

    #endregion
}
