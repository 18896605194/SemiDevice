using xyz.Components.Interfaces;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// E87 的几套状态码（照 SEMI E87，上报 Host 用这些数，都是 U1）。
/// </summary>
internal static class E87Codes
{
    #region CarrierIDStatus

    public const byte IdNotRead = 0;
    public const byte IdWaitingForHost = 1;
    public const byte IdVerified = 2;
    public const byte IdVerifyFailed = 3;

    #endregion

    #region SlotMapStatus

    public const byte MapNotRead = 0;
    public const byte MapWaitingForHost = 1;
    public const byte MapVerified = 2;
    public const byte MapVerifyFailed = 3;

    #endregion

    #region CarrierAccessingStatus

    public const byte NotAccessed = 0;
    public const byte InAccess = 1;
    public const byte CarrierComplete = 2;
    public const byte CarrierStopped = 3;

    #endregion

    #region PortTransferState

    public const byte OutOfService = 0;
    public const byte TransferBlocked = 1;
    public const byte ReadyToLoad = 2;
    public const byte ReadyToUnload = 3;

    #endregion

    #region SlotStatus（跟设备的 SlotState 数值一样）

    public const byte SlotEmpty = 1;
    public const byte SlotNotEmpty = 2;
    public const byte SlotCorrectlyOccupied = 3;

    #endregion

    #region Reason（跟载具转换一起报）

    /// <summary>#14：槽图读到了，等 Host 核对。</summary>
    public const byte ReasonVerificationNeeded = 0;

    /// <summary>#14：设备按 Host 给的槽图核对没通过。</summary>
    public const byte ReasonVerificationUnsuccessful = 1;

    /// <summary>#7：读码失败。</summary>
    public const byte ReasonReadFail = 2;

    /// <summary>#9 / #16：Host 取消了这个载具。</summary>
    public const byte ReasonHostCancel = 5;

    /// <summary>#9：载具号跟机内另一个载具重了，设备自己取消。</summary>
    public const byte ReasonDuplicateId = 6;

    #endregion
}

/// <summary>
/// E87 的一个载具（Host 看到的那一个）：Host 预告的（Bind、CarrierNotification，还没到）或者到了端口读了码的。
/// 核对的进展（ID、槽图）、取放进展按 E87 走；Host 给的期望（槽图、片号表）存着，设备读到槽图时拿来核对。只在 E87 的锁里改。
/// </summary>
internal sealed class E87Carrier
{
    public required string Id { get; init; }

    /// <summary>关联的端口（Bind 的、读码认出来的）；Host 只预告了、没定端口的为 null。</summary>
    public E87Port? Port { get; set; }

    /// <summary>已经放到端口上了（Bind 预告的载具到之前是 false）。</summary>
    public bool Arrived { get; set; }

    /// <summary>Bind 来的（端口为它预约着）。</summary>
    public bool Bound { get; set; }

    public byte IdStatus { get; set; } = E87Codes.IdNotRead;

    public byte SlotMapStatus { get; set; } = E87Codes.MapNotRead;

    public byte Accessing { get; set; } = E87Codes.NotAccessed;

    /// <summary>槽数（Host 给的，或者读槽图后按读到的）；不知道为 null。</summary>
    public byte? Capacity { get; set; }

    /// <summary>片数（Host 给的，或者读槽图后数的）；不知道为 null。</summary>
    public byte? SubstrateCount { get; set; }

    /// <summary>读到的槽图（SlotStatus 码，下标 0 是第 1 槽）；没读为 null。</summary>
    public byte[]? SlotMap { get; set; }

    /// <summary>Host 给的槽图：读到的跟它一样就由设备认定（不用再等 Host）。</summary>
    public byte[]? ExpectedSlotMap { get; set; }

    /// <summary>Host 给的片号表（每槽 批次号 + 片号）：槽图认定后写进晶圆账。</summary>
    public List<(string LotId, string SubstrateId)>? ContentMap { get; set; }

    /// <summary>用途（PRODUCT、TEST……），Host 给的；没给为空。</summary>
    public string Usage { get; set; } = string.Empty;

    /// <summary>在哪（端口名）；还没到为空。</summary>
    public string LocationId => Arrived && Port is not null ? Port.Device.Name : string.Empty;
}

/// <summary>
/// E87 的一个端口：设备上的 LoadPort（ILoadPort）加上只有 Host 那边才有的设定和状态——
/// 停没停用（S3F25）、预约、关联的载具、这一盒是不是被取消了、上次报的搬运状态。只在 E87 的锁里改。
/// </summary>
internal sealed class E87Port
{
    public required ILoadPort Device { get; init; }

    /// <summary>PortID：按 sc.xml 里 LoadPort 的先后，从 1 开始。</summary>
    public required byte Id { get; init; }

    /// <summary>Host 没让停用（S3F25 OutOfService 之前一直是 true）。</summary>
    public bool InService { get; set; } = true;

    /// <summary>上次报给 Host 的搬运状态；还没报过为 null。</summary>
    public byte? TransferState { get; set; }

    public bool Reserved { get; set; }

    /// <summary>关联的载具（ASSOCIATED）。</summary>
    public E87Carrier? Carrier { get; set; }

    /// <summary>端口上放着载具（设备回调认的）。</summary>
    public bool Present { get; set; }

    /// <summary>没有预告、读码失败：等 Host 带端口号 ProceedWithCarrier 给 ID，或者取消。</summary>
    public bool ReadFailed { get; set; }

    /// <summary>这一盒不要了（Host 取消、核对不过、载具号重了）：卸下来等取走。</summary>
    public bool Rejected { get; set; }

    public byte AssociationState => Carrier is null ? (byte)0 : (byte)1;

    public byte ReservationState => Reserved ? (byte)1 : (byte)0;

    public byte AccessMode => Device.IsAutoMode ? (byte)1 : (byte)0;
}

/// <summary>E39 对象类型 Carrier：E87 的载具。</summary>
internal sealed class E87CarrierType : IE39ObjectType
{
    private readonly E87Component _owner;

    public E87CarrierType(E87Component owner)
    {
        _owner = owner;
    }

    public string TypeName => "Carrier";

    public IReadOnlyList<string> AttributeNames { get; } =
    [
        "ObjType", "ObjID", "Capacity", "CarrierAccessingStatus", "CarrierIDStatus", "ContentMap", "LocationID", "SlotMap",
        "SlotMapStatus", "SubstrateCount", "Usage",
    ];

    public IReadOnlyList<string> ObjectIds()
    {
        return _owner.CarrierIds();
    }

    public bool TryGetAttribute(string objectId, string attribute, out SecsItem value)
    {
        return _owner.TryGetCarrierAttribute(objectId, attribute, out value);
    }
}

/// <summary>E39 对象类型 Port：E87 的端口（ObjID 是 PortID）。</summary>
internal sealed class E87PortType : IE39ObjectType
{
    private readonly E87Component _owner;

    public E87PortType(E87Component owner)
    {
        _owner = owner;
    }

    public string TypeName => "Port";

    public IReadOnlyList<string> AttributeNames { get; } =
    [
        "ObjType", "ObjID", "PortAccessMode", "PortAssociationState", "PortReservationState", "PortStateInfo", "PortTransferState",
    ];

    public IReadOnlyList<string> ObjectIds()
    {
        return _owner.PortIds();
    }

    public bool TryGetAttribute(string objectId, string attribute, out SecsItem value)
    {
        return _owner.TryGetPortAttribute(objectId, attribute, out value);
    }
}
