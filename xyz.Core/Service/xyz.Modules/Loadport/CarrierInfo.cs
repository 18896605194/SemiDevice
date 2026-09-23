using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 载具（FOUP）对象
/// </summary>
public sealed record CarrierInfo
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Location { get; init; }

    /// <summary>花篮槽数。</summary>
    public int Capacity { get; init; }

    public string CarrierId { get; init; } = string.Empty;

    public string? LotId { get; init; }

    public CarrierIdStatus IdStatus { get; init; } = CarrierIdStatus.NotRead;

    /// <summary>槽图认定到哪一步。</summary>
    public CarrierSlotMapStatus SlotMapStatus { get; init; } = CarrierSlotMapStatus.NotRead;

    /// <summary>取放到哪一步。</summary>
    public CarrierAccessStatus AccessStatus { get; init; } = CarrierAccessStatus.NotAccessed;

    /// <summary>放上端口的时刻。</summary>
    public DateTime ArrivedAt { get; init; } = DateTime.Now;

    /// <summary>最近一次状态变化的时刻。</summary>
    public DateTime UpdatedAt { get; init; } = DateTime.Now;
}
