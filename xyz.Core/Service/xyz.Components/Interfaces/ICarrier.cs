using xyz.Components.Models;
using xyz.Drivers.Loadport;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 载具接口
/// </summary>
public interface ICarrier
{

    bool IsArrived { get; }

    string? CarrierId { get; }

    IReadOnlyList<SlotState> SlotMap { get; }

    /// <summary>
    /// 这一盒载具的快照；端口上没载具为 null
    /// </summary>
    CarrierInfo? Info { get; }

    /// <summary>
    /// 载具这边认可了、能取放片：没接 EAP 读到就算，接了 EAP 要 Host 认定槽图
    /// </summary>
    bool IsAccepted { get; }

    bool ReadId();

    void SetId(string carrierId);

    void UpdateStatus(CarrierIdStatus? idStatus, CarrierSlotMapStatus? slotMapStatus);

    void NoteComplete();

    #region 端口调的（只有 LoadPort 模块调，EAP 别调）

    /// <summary>
    /// 端口把自己、读码器（没配为 null）、E87 上报的入队口交给它；端口组件初始化时调
    /// </summary>
    void Attach(ILoadPort port, ICarrierIdReader? reader, Action<Action<IE87Callback>> enqueue);

    /// <summary>
    /// 每拍喂状态查询的在位、到位两位（null = 没查到），判放上 / 拿走
    /// </summary>
    void Sense(bool? isPresent, bool? isPlaced);

    /// <summary>
    /// 设备主动报的放上 / 拿走（PODON / PODOF）
    /// </summary>
    void SetDeviceReportedPlaced(bool placed);

    /// <summary>
    /// Load 带回了 Mapping 结果
    /// </summary>
    void NoteMapped(IReadOnlyList<SlotState> slotMap);

    /// <summary>
    /// Load 好了，开始取放
    /// </summary>
    void NoteLoaded();

    /// <summary>
    /// Unload 好了，取放结束
    /// </summary>
    void NoteUnloaded();

    /// <summary>
    /// 端口动作没做成，取放途中出错
    /// </summary>
    void NoteFault();

    #endregion
}
