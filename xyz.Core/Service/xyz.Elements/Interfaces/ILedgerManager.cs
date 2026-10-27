using xyz.Elements.Enums;
using xyz.Elements.Models;
using xyz.Database.Wafers;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 晶圆账
/// </summary>
public interface ILedgerManager
{
    bool IsEnable { get; }

    /// <summary>
    /// 一个站点各槽的片，没片是 null
    /// </summary>
    IReadOnlyList<WaferInfo?> GetSlots(string module);

    /// <summary>
    /// 一个槽上的片，没片是 null
    /// </summary>
    WaferInfo? Get(string module, int slot);

    /// <summary>
    /// 改片号，槽上没片返回 false
    /// </summary>
    bool SetWaferId(string module, int slot, string waferId);

    /// <summary>
    /// 改批次，槽上没片返回 false
    /// </summary>
    bool SetLotId(string module, int slot, string? lotId);

    /// <summary>
    /// 登记过的站点和槽数
    /// </summary>
    IReadOnlyList<(string Module, int SlotCount)> Locations { get; }

    /// <summary>
    /// 这个站点是不是 LoadPort
    /// </summary>
    bool IsLoadPort(string module);

    /// <summary>
    /// E90 挂上来的回调（片对象跟着账走）
    /// </summary>
    IE90Callback? E90Callback { get; set; }

    /// <summary>
    /// 按片的 Id 找，没有是 null
    /// </summary>
    WaferInfo? FindById(Guid id);

    /// <summary>
    /// 登记一个放片的位置（腔体、机械手手指）
    /// </summary>
    void RegisterLocation(string module, int slotCount);

    /// <summary>
    /// 登记一个 LoadPort
    /// </summary>
    void RegisterLoadPort(string module, int slotCount);

    /// <summary>
    /// 这个槽上有没有片
    /// </summary>
    bool HasWafer(string module, int slot);

    /// <summary>
    /// 改工艺状态，槽上没片返回 false
    /// </summary>
    bool SetProcessState(string module, int slot, WaferProcessState state);

    /// <summary>
    /// 账跟设备报的有没有片对一下
    /// </summary>
    bool Verify(string module, int slot, bool deviceHasWafer);

    /// <summary>
    /// 给一个站点上的片都填上载具号，返回改了几片
    /// </summary>
    int SetCarrierIdOn(string module, string? carrierId);

    /// <summary>
    /// 清掉一个站点上的全部片
    /// </summary>
    int Clear(string module);

    /// <summary>
    /// 按扫出来的槽图落账
    /// </summary>
    int ApplySlotMap(string module, IReadOnlyList<WaferStatus?> slots, string? carrierId = null, string? lotId = null);

    /// <summary>
    /// 把一片从一个槽挪到另一个槽
    /// </summary>
    bool Move(string fromModule, int fromSlot, string toModule, int toSlot);

    /// <summary>
    /// 确认来源仍是预期晶圆后移账；expectedWaferId 为已有 WaferInfo.Id。
    /// 身份核对与位置更新在同一把账本锁内完成，不符合时不修改账本。
    /// </summary>
    bool Move(string fromModule, int fromSlot, string toModule, int toSlot, Guid expectedWaferId);

    /// <summary>
    /// 一个站点上有几片
    /// </summary>
    int CountWafers(string module);

    /// <summary>
    /// 按片号找，没有是 null
    /// </summary>
    WaferInfo? Find(string waferId);

    /// <summary>
    /// 人工建片（账单调整页）
    /// </summary>
    WaferAdjustResult ManualCreate(string module, int slot, string waferId, string operatorName, string? reason);

    /// <summary>
    /// 人工移账
    /// </summary>
    WaferAdjustResult ManualMove(string fromModule, int fromSlot, string toModule, int toSlot, string operatorName, string? reason);

    /// <summary>
    /// 人工删账
    /// </summary>
    WaferAdjustResult ManualDelete(string module, int slot, string operatorName, string? reason);

    /// <summary>
    /// 最近的人工调整记录
    /// </summary>
    IReadOnlyList<WaferAdjustmentEntity> GetRecentAdjustments();
}
