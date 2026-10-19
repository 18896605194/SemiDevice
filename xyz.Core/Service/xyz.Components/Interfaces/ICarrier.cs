using xyz.Drivers.Loadport;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 载具接口
/// </summary>
public interface ICarrier : ICarrierAccess
{
    bool ReadId();

    void SetId(string carrierId);

    void UpdateStatus(CarrierIdStatus? idStatus, CarrierSlotMapStatus? slotMapStatus);

    /// <summary>
    /// 标记这个载具已经完成
    /// </summary>
    void NoteComplete();

    /// <summary>
    /// 标记已经完成根据这个  carrierId
    /// </summary>
    /// <param name="carrierId"></param>
    /// <returns></returns>
    HandleResult NoteComplete(string carrierId);

    #region 端口调的（只有 LoadPort 模块调，EAP 别调）

    /// <summary>
    /// 端口把自己、读码器（没配为 null）、E87 上报的入队口交给它；端口组件初始化时调
    /// </summary>
    void Attach(ILoadPortAccess port, ICarrierIdReader? reader, Action<Action<IE87Callback>> enqueue);

    /// <summary>
    /// 在位、到位
    /// </summary>
    void Sense(bool? isPresent, bool? isPlaced);

    /// <summary>
    /// 设备主动报的放上 / 拿走（PODON / PODOF）
    /// </summary>
    void SetDeviceReportedPlaced(bool placed);

    /// <summary>
    /// Load 带回了 Mapping 结果
    /// </summary>
    void UpdateSlotMap(IReadOnlyList<SlotState> slotMap);

    /// <summary>
    /// Load 好了，开始取放
    /// </summary>
    void StartAccess();

    /// <summary>
    /// Unload 好了，取放结束
    /// </summary>
    void EndAccess();

    /// <summary>
    /// 端口动作没做成，取放途中出错
    /// </summary>
    void MarkAccessStopped();

    #endregion
}
