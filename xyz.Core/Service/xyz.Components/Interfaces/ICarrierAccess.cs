using xyz.Components.Models;
using xyz.Drivers.Loadport;

namespace xyz.Components.Interfaces;

/// <summary>
/// 载具对外的业务访问
/// </summary>
public interface ICarrierAccess
{
    /// <summary>载具是否已经到位。</summary>
    bool IsArrived { get; }

    /// <summary>当前载具号；未读到或载具已移走时为 null。</summary>
    string? CarrierId { get; }

    /// <summary>最近一次 Mapping 的只读结果，下标 0 对应第 1 槽；未 Mapping 或载具已移走时为空。</summary>
    IReadOnlyList<SlotState> SlotMap { get; }

    /// <summary>当前载具的状态快照；端口上没有载具时为 null。</summary>
    CarrierInfo? Info { get; }

    /// <summary>载具是否被认可；接了 EAP 时要求槽图通过 Host 认定，端口准入条件另行检查。</summary>
    bool IsAccepted { get; }
}
