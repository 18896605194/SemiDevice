using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 晶圆账全貌（设置 → 账单调整页）：能调整的位置——所有机械手 sc.xml 里 Stations 配的站点合并，再加上机械手自己（手指），
/// 以及每个位置的槽和槽上的片。先后：机械手在前，站点按各机械手 Stations 里的先后，多台机械手共用的站点只列一次。
/// 账一变（建片、移片、改信息、删片）后端就推一条 <see cref="WaferLedgerChangedDto"/>（token = EventToken，不留存），界面收到后自己重拉。
/// </summary>
public class WaferLedgerDto
{
    public const string EventToken = "WaferLedger";

    /// <summary>
    /// 晶圆账开着没有（sc.xml 没配 WaferManager 或 IsEnable=False 时为 false，位置表是空的）。
    /// </summary>
    public bool IsEnabled { get; set; }

    public List<WaferLocationDto> Locations { get; set; } = [];
}

/// <summary>
/// 位置的种类：界面按它分组；机械手的槽叫"手指"，LoadPort 的槽大号在上。
/// </summary>
public enum WaferLocationKind
{
    Robot,
    LoadPort,
    Chamber,
    Other,
}

/// <summary>
/// 一个能放片的位置（一个模块）和它的槽。
/// </summary>
public class WaferLocationDto
{
    public string Name { get; set; } = string.Empty;

    public WaferLocationKind Kind { get; set; }

    /// <summary>
    /// LoadPort 上的载具号；其他位置为空。
    /// </summary>
    public string? CarrierId { get; set; }

    /// <summary>
    /// 每个槽一项，槽号从 1 起；机械手的槽号就是手指号。
    /// </summary>
    public List<WaferSlotDto> Slots { get; set; } = [];
}

/// <summary>
/// 一个槽：空槽时 Wafer 为 null。
/// </summary>
public class WaferSlotDto
{
    public int Slot { get; set; }

    public WaferDto? Wafer { get; set; }

    /// <summary>
    /// 两份槽位账一样不一样（槽号、有没有片、片号、批次、载具、物理状态、工艺状态、来源 LoadPort 和槽位逐个比）。
    /// 模块状态推送靠它判断账变了没有：账一变，下一拍就把新的推给界面。
    /// </summary>
    public static bool SameSlots(IReadOnlyList<WaferSlotDto> left, IReadOnlyList<WaferSlotDto> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (left[index].Slot != right[index].Slot || !SameWafer(left[index].Wafer, right[index].Wafer))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameWafer(WaferDto? left, WaferDto? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.WaferId == right.WaferId
            && left.LotId == right.LotId
            && left.CarrierId == right.CarrierId
            && left.Status == right.Status
            && left.ProcessState == right.ProcessState
            && left.SourceLoadPort == right.SourceLoadPort
            && left.SourceSlot == right.SourceSlot;
    }
}

/// <summary>
/// 槽上的一片。
/// </summary>
public class WaferDto
{
    public string WaferId { get; set; } = string.Empty;

    public string? LotId { get; set; }

    public string? CarrierId { get; set; }

    /// <summary>
    /// 物理状态：Normal / Crossed / Double / Dummy / Unknown。
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 工艺状态：Idle / InProcess / Completed / Failed / Aborted。
    /// </summary>
    public string ProcessState { get; set; } = string.Empty;

    /// <summary>
    /// 来源 LoadPort 的模块名（如 LoadPort1）；不是在 LoadPort 上建的片为 null。圆片上显示"LoadPort1-25"用。
    /// </summary>
    public string? SourceLoadPort { get; set; }

    /// <summary>
    /// 来源 LoadPort 的槽号；不是在 LoadPort 上建的片为 0。
    /// </summary>
    public int SourceSlot { get; set; }
}

/// <summary>
/// 账变了的通知：只说哪个位置的账变了，不带账（变动可能一下来一串，比如整篮 Mapping，界面收到后合并着重拉一次）。
/// </summary>
public class WaferLedgerChangedDto
{
    public string Module { get; set; } = string.Empty;
}

/// <summary>
/// 一条人工调整记录（账单调整页右边的"调整记录"）。
/// </summary>
public class WaferAdjustmentDto
{
    public DateTime Time { get; set; }

    /// <summary>
    /// Move（移动）/ Delete（删除）/ Create（新建，补账）。
    /// </summary>
    public string Action { get; set; } = string.Empty;

    public string WaferId { get; set; } = string.Empty;

    /// <summary>
    /// 从哪移走；删除、新建时就是删的、建的那个位置。
    /// </summary>
    public string FromModule { get; set; } = string.Empty;

    public int FromSlot { get; set; }

    /// <summary>
    /// 移到哪；删除、新建时为空。
    /// </summary>
    public string? ToModule { get; set; }

    public int? ToSlot { get; set; }

    public string Operator { get; set; } = string.Empty;

    public string? Reason { get; set; }
}

/// <summary>
/// 人工移账请求。code-first gRPC 的请求参数必须是消息类，故包一层。
/// </summary>
[ProtoContract]
public class WaferMoveRequest
{
    [ProtoMember(1)]
    public string FromModule { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int FromSlot { get; set; }

    [ProtoMember(3)]
    public string ToModule { get; set; } = string.Empty;

    [ProtoMember(4)]
    public int ToSlot { get; set; }

    /// <summary>
    /// 原因（选填），记进调整记录。
    /// </summary>
    [ProtoMember(5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 操作人（客户端当前用户），记进调整记录。
    /// </summary>
    [ProtoMember(6)]
    public string Operator { get; set; } = string.Empty;
}

/// <summary>
/// 人工补账请求：在空槽上建一片，片号由人填，其余按默认（正常片、未处理）。
/// </summary>
[ProtoContract]
public class WaferCreateRequest
{
    [ProtoMember(1)]
    public string Module { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int Slot { get; set; }

    [ProtoMember(3)]
    public string WaferId { get; set; } = string.Empty;

    [ProtoMember(4)]
    public string Reason { get; set; } = string.Empty;

    [ProtoMember(5)]
    public string Operator { get; set; } = string.Empty;
}

/// <summary>
/// 人工删账请求。
/// </summary>
[ProtoContract]
public class WaferDeleteRequest
{
    [ProtoMember(1)]
    public string Module { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int Slot { get; set; }

    [ProtoMember(3)]
    public string Reason { get; set; } = string.Empty;

    [ProtoMember(4)]
    public string Operator { get; set; } = string.Empty;
}
