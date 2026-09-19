using xyz.Drivers.Loadport;

namespace xyz.Modules;

public interface ILoadPort
{
    #region 状态

    string Name { get; }

    int State { get; }

    bool IsPodPlaced { get; }

    /// <summary>
    /// Auto/Manual（LoadPort 的 Access Mode）：Auto = 搬运车经 E84 自动交接，Manual = 人工放取。
    /// </summary>
    bool IsAutoMode { get; }

    string? CarrierId { get; }

    IReadOnlyList<SlotState> SlotMap { get; }

    #endregion

    #region 动作

    ModuleOperation? Load();

    ModuleOperation? Unload();

    ModuleOperation? Home();

    /// <summary>
    /// 初始化：子组件先初始化，再 Home。
    /// </summary>
    ModuleOperation? Init();

    ModuleOperation? Reset();

    ModuleOperation? Abort();

    ModuleOperation? Clamp();

    ModuleOperation? Unclamp();

    /// <summary>
    /// 切 Auto/Manual（内部模式位，不经设备协议）；变了回调 EAP AutoModeChanged。
    /// </summary>
    void SetAutoMode(bool autoMode);

    /// <summary>
    /// 发起一次读码（非阻塞）；结果经 CarrierId 与 E87 的 CarrierIdRead/CarrierIdReadFailed 出。
    /// </summary>
    bool ReadCarrierId();

    void SetCarrierId(string carrierId);

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

    /// <summary>
    /// 上层作业判定这个载具干完了，转成 E87 的 CarrierComplete 上报。
    /// </summary>
    void NoteCarrierComplete();

    #endregion
}
