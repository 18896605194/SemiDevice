using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

public interface ILoadPort
{
    #region 状态

    string Name { get; }

    int State { get; }

    /// <summary>
    /// 载具
    /// </summary>
    ICarrier _carrier { get; }

    /// <summary>
    /// Auto/Manual Auto = 搬运车经 E84 自动交接，Manual = 人工放取。
    /// </summary>
    bool IsAutoMode { get; }

    /// <summary>
    /// 花篮槽数（E87 载具的 Capacity）
    /// </summary>
    int SlotCount { get; }

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

    #endregion

    #region 动作

    ModuleOperation? InitModule();

    /// <summary>
    /// Load：端口自己按 状态 → 资源 → 互锁 查，被拒带原因（错误码 + 参数）返回、端口不动，调用方不用先查；通过就是发起的动作。
    /// </summary>
    HandleResult<ModuleOperation> Load();

    ModuleOperation? Unload();

    ModuleOperation? Home();

    ModuleOperation? Reset();

    ModuleOperation? Abort();

    ModuleOperation? Clamp();

    ModuleOperation? Unclamp();

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
}
