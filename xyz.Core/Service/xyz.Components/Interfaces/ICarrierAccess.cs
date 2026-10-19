using xyz.Components.Models;
using xyz.Drivers.Loadport;

namespace xyz.Components.Interfaces;

/// <summary>
/// 载具对外的业务访问
/// </summary>
public interface ICarrierAccess
{
    /// <summary>
    /// 是否已经到位
    /// </summary>
    bool IsArrived { get; }

    /// <summary>
    /// 载具号
    /// </summary>
    string? CarrierId { get; }

    /// <summary>
    /// map 结果
    /// </summary>
    IReadOnlyList<SlotState> SlotMap { get; }

    /// <summary>
    /// 载具信息
    /// </summary>
    CarrierInfo? Info { get; }

    /// <summary>
    /// 是否被认可
    /// </summary>
    bool IsAccepted { get; }
}
