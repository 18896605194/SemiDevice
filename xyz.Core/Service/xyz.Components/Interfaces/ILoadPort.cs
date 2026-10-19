using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

public interface ILoadPort : ITransferStation, IJobAccess
{
    #region 状态

    /// <summary>
    /// 载具接口
    /// </summary>
    ICarrier _carrier { get; }

    bool IsAutoMode { get; }

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

    HandleResult<ModuleOperation> Load();

    HandleResult<ModuleOperation> Unload();

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
    /// </summary>
    IE84Provider? E84Provider { get; set; }

    #endregion

    #region 给服务用的（手动页、主界面）

    /// <summary>
    /// 取片顺序
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

    HandleResult<ModuleOperation> UnloadManually();

    HandleResult? CheckReset();

    HandleResult? CheckAbort();

    LoadPortDto CreateStateDto();

    #endregion
}
