namespace xyz.Components.Wafers;

/// <summary>
/// 账上的一片：身份、当前在哪、从哪来、状态。
/// 只有 WaferManager 能改（属性都是 internal set），外部查询拿到的是快照副本。
/// </summary>
public sealed class WaferInfo
{
    internal WaferInfo(string module, int slot, WaferStatus status, string? carrierId, string? lotId)
    {
        Module = module;
        Slot = slot;
        OriginModule = module;
        OriginSlot = slot;
        OriginCarrierId = carrierId;
        CarrierId = carrierId;
        LotId = lotId;
        Status = status;
        WaferId = $"{carrierId ?? module}.{slot:00}";
        CreatedAt = DateTime.Now;
        UpdatedAt = CreatedAt;
    }

    private WaferInfo(WaferInfo source)
    {
        Id = source.Id;
        WaferId = source.WaferId;
        Module = source.Module;
        Slot = source.Slot;
        OriginModule = source.OriginModule;
        OriginSlot = source.OriginSlot;
        OriginCarrierId = source.OriginCarrierId;
        CarrierId = source.CarrierId;
        LotId = source.LotId;
        Status = source.Status;
        ProcessState = source.ProcessState;
        CreatedAt = source.CreatedAt;
        UpdatedAt = source.UpdatedAt;
    }

    /// <summary>内部唯一标识，建片时生成，一直跟到删片（入库主键）。</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>业务片号，默认按"载具或模块.槽位"生成，读码或 Host 改写后更新。</summary>
    public string WaferId { get; internal set; }

    /// <summary>当前所在模块（机械手手臂也算模块）。</summary>
    public string Module { get; internal set; }

    /// <summary>当前所在槽位，从 1 开始（手臂号即槽位号）。</summary>
    public int Slot { get; internal set; }

    /// <summary>建片时所在模块。</summary>
    public string OriginModule { get; }

    /// <summary>建片时所在槽位。</summary>
    public int OriginSlot { get; }

    /// <summary>建片时所在载具；不在载具里建的为 null。</summary>
    public string? OriginCarrierId { get; }

    /// <summary>当前所属载具；片离开载具后仍保留，便于回篮。</summary>
    public string? CarrierId { get; internal set; }

    /// <summary>批次号。</summary>
    public string? LotId { get; internal set; }

    /// <summary>物理状态（正常/交叉/叠片/陪片）。</summary>
    public WaferStatus Status { get; internal set; }

    /// <summary>工艺状态。</summary>
    public WaferProcessState ProcessState { get; internal set; }

    /// <summary>建片时刻。</summary>
    public DateTime CreatedAt { get; }

    /// <summary>最后一次变动时刻（移动或改状态）。</summary>
    public DateTime UpdatedAt { get; internal set; }

    /// <summary>
    /// 复制一份快照：查询返回副本，外部拿不到账上的活对象。
    /// </summary>
    internal WaferInfo Clone()
    {
        return new WaferInfo(this);
    }

    public override string ToString()
    {
        return $"{WaferId}@{Module}.{Slot:00}({Status})";
    }
}
