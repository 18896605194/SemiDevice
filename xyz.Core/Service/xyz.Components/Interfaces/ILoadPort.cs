using xyz.Components.Components;
using xyz.Components.Enums;

namespace xyz.Components.Interfaces;

public interface ILoadPort
{
    #region 状态

    string Name { get; }

    int State { get; }

    /// <summary>
    /// 端口上这一盒载具：到了没有、ID、槽图，以及读码、核对进展、干完了这几条命令。载具的事都在它那儿，端口不再另放一份。
    /// </summary>
    ICarrier Carrier { get; }

    /// <summary>
    /// Auto/Manual（LoadPort 的 Access Mode）：Auto = 搬运车经 E84 自动交接，Manual = 人工放取。
    /// </summary>
    bool IsAutoMode { get; }

    /// <summary>花篮槽数（E87 载具的 Capacity）。</summary>
    int SlotCount { get; }

    /// <summary>载具 Load 好了（门开着、能取放片，正被机械手服务时也算），Unload 之前一直是 true。</summary>
    bool IsLoaded { get; }

    /// <summary>端口空闲：不在做动作、不在被机械手服务（载具 Unload 好了、等人或天车取走时也是空闲）。</summary>
    bool IsIdle { get; }

    /// <summary>
    /// 端口自己判的搬运状态（不看 EAP）：停用、离线、没初始化、出错 → OutOfService；在忙 → TransferBlocked；
    /// 空闲没载具 → ReadyToLoad；空闲有载具、这一盒干完或中断了 → ReadyToUnload。E87 在这上面再加 Host 的设定。
    /// </summary>
    LoadPortTransferState LocalTransferState { get; }

    #endregion

    #region 动作

    ModuleOperation? Load();

    ModuleOperation? Unload();

    ModuleOperation? Home();

    /// <summary>
    /// 模块初始化（动硬件）：回原点（Home）。人或调度才调，开机不调。
    /// </summary>
    ModuleOperation? InitModule();

    ModuleOperation? Reset();

    ModuleOperation? Abort();

    ModuleOperation? Clamp();

    ModuleOperation? Unclamp();

    /// <summary>
    /// 切 Auto/Manual（内部模式位，不经设备协议）；变了回调 EAP AutoModeChanged。
    /// </summary>
    void SetAutoMode(bool autoMode);

    #endregion

    #region EAP 口子

    /// <summary>
    /// E87 载具管理：载具到达/移走、ID、Mapping、动作完成等由模块上报。
    /// </summary>
    IE87Callback? E87Callback { get; set; }

    /// <summary>
    /// E84 自动交接：交接开始/结束/超时由模块上报。
    /// </summary>
    IE84Callback? E84Callback { get; set; }

    /// <summary>
    /// E84 握手期间设备侧反查 EAP（端口搬运状态、自动模式）。
    /// HO_AVBL 由 E84 组件按端口给的许可自己开关，EAP 不直接置。
    /// </summary>
    IE84Provider? E84Provider { get; set; }

    #endregion
}
