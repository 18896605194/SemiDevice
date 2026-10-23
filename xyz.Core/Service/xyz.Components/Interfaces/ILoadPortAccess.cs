using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// LoadPort 给 EAP 用的那一面（E87 管载具、E84 交接）
/// </summary>
public interface ILoadPortAccess : IModule
{
    #region 状态

    /// <summary>
    /// 载具接口
    /// </summary>
    ICarrier _carrier { get; }

    /// <summary>
    /// 槽数
    /// </summary>
    int SlotCount { get; }

    /// <summary>
    /// 是不是自动模式
    /// </summary>
    bool IsAutoMode { get; }

    /// <summary>
    /// 载具是不是 Load 好了
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// 端口空闲
    /// </summary>
    bool IsIdle { get; }

    /// <summary>
    /// E87状态
    /// </summary>
    LoadPortTransferState LocalTransferState { get; }

    #endregion

    #region 动作

    /// <summary>
    /// Load 载具
    /// </summary>
    HandleResult<ModuleRoutine> Load();

    /// <summary>
    /// Unload 载具
    /// </summary>
    HandleResult<ModuleRoutine> Unload();

    /// <summary>
    /// 现在能不能 Unload，不能的带原因
    /// </summary>
    HandleResult? CheckUnload();

    /// <summary>
    /// 切自动 / 手动模式
    /// </summary>
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
    /// </summary>
    IE84Provider? E84Provider { get; set; }

    #endregion
}
