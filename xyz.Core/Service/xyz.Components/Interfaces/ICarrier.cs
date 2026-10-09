using xyz.Drivers.Loadport;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 端口上这一盒载具（FOUP）：设备侧的载具事实，加上 EAP、Job 能下的几条命令。
/// 实现在模块层（CarrierComponent），每个 LoadPort 一个，经 <see cref="ILoadPort.Carrier"/> 拿到；
/// E87 的载具核对、Job 的取片放片只依赖这一小块，不用依赖整个 ILoadPort。
/// 只管"这个端口上的这一盒"，不是载具表，也不是 E39 的 Carrier 对象。
/// </summary>
public interface ICarrier
{
    /// <summary>
    /// 载具到了：后台按 sc.xml PresenceSource 判出来的结果（状态查询的在位、到位两位，或设备上报的 PODON/PODOF），不是传感器原始值。
    /// </summary>
    bool IsArrived { get; }

    /// <summary>载具 ID；没读到、没载具时为 null。</summary>
    string? CarrierId { get; }

    /// <summary>最近一次 Mapping 结果，下标 0 对应第 1 槽；未 Mapping 或载具已移走为空列表。</summary>
    IReadOnlyList<SlotState> SlotMap { get; }

    /// <summary>
    /// 发起一次读码（非阻塞）；结果经 CarrierId 与 E87 的 CarrierIdRead / CarrierIdReadFailed 出。
    /// 没发起成功（没配读头、读头没连上、上一次还没读完）返回 false。
    /// </summary>
    bool ReadId();

    /// <summary>
    /// 改写载具 ID：Host 确认的 ID 与读到的不一致时以 Host 为准，改完即认定；不回调 EAP。
    /// </summary>
    void SetId(string carrierId);

    /// <summary>
    /// EAP 把 Host 核对载具的进展写回设备侧（给了的才改）：等 Host、核对通过、没通过。界面上看到的 ID / 槽图状态跟 Host 那边一致。
    /// </summary>
    void UpdateStatus(CarrierIdStatus? idStatus, CarrierSlotMapStatus? slotMapStatus);

    /// <summary>
    /// 上层作业判定这个载具干完了，转成 E87 的 CarrierComplete 上报。
    /// </summary>
    void NoteComplete();
}
