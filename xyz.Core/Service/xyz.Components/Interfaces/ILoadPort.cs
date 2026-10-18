using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

public interface ILoadPort : ITransferStation
{
    #region 状态

    /// <summary>
    /// 载具
    /// </summary>
    ICarrier _carrier { get; }

    /// <summary>
    /// Auto/Manual Auto = 搬运车经 E84 自动交接，Manual = 人工放取。
    /// </summary>
    bool IsAutoMode { get; }

    /// <summary>
    /// 载具 Load 好了
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// 端口空闲：不在做动作、不在被机械手服务（载具 Unload 好了、等人或天车取走时也是空闲）
    /// </summary>
    bool IsIdle { get; }

    /// <summary>
    /// E87状态
    /// </summary>
    LoadPortTransferState LocalTransferState { get; }

    /// <summary>
    /// 载具到了、Load 好了、槽图认定了：能给 Job 定片
    /// </summary>
    bool CanAssignCarrierToJob { get; }

    #endregion

    #region 动作

    // 动作入口：端口自己按 状态 → 资源 → 互锁 查，被拒带原因（错误码 + 参数）返回、端口不动，调用方不用先查；通过就是发起的动作。
    // 复位、中止跟着组件基类，返回动作或 null。

    HandleResult<ModuleOperation> Load();

    /// <summary>Unload（自动跑货口径：SC AutoRunMapOnUnload 开着就带图对账）。</summary>
    HandleResult<ModuleOperation> Unload();

    /// <summary>Unload 能不能做（只查不动，回 Host 之前用）；null = 能。</summary>
    HandleResult? CheckUnload();

    HandleResult<ModuleOperation> Home();

    HandleResult<ModuleOperation> Clamp();

    HandleResult<ModuleOperation> Unclamp();

    void SetAutoMode(bool autoMode);

    #endregion

    #region EAP 口子

    /// <summary>
    /// E87 载具管理
    /// </summary>
    IE87Callback? E87Callback { get; set; }

    /// <summary>
    /// E84 自动交接
    /// </summary>
    IE84Callback? E84Callback { get; set; }

    /// <summary>
    /// E84 握手期间设备侧反查 EAP（端口搬运状态、自动模式）。
    /// HO_AVBL 由 E84 组件按端口给的许可自己开关，EAP 不直接置。
    /// </summary>
    IE84Provider? E84Provider { get; set; }

    #endregion

    #region 给服务用的（手动页、主界面）

    /// <summary>
    /// 取片先后（从上往下 / 从下往上）
    /// </summary>
    SlotPickOrder PickOrder { get; }

    /// <summary>
    /// Load 等到做完的上限（ms）
    /// </summary>
    int LoadTimeout { get; }

    /// <summary>
    /// Unload 等到做完的上限（ms）
    /// </summary>
    int UnloadTimeout { get; }

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
    /// 手动 Unload（只发 CULOD，不扫图对账）
    /// </summary>
    HandleResult<ModuleOperation> UnloadManually();

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
    LoadPortDto CreateStateDto();

    #endregion
}
