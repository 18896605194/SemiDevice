using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Drivers.Loadport;
using xyz.Secs.SecsII;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// 载具管理（SEMI E87，sc.xml 的 Eap 下的 E87 节点）：跟 Host 核对载具的过程和端口在 Host 眼里的状态，都在 EAP 这一侧。
/// ① 载具：到了读码 → 有预告（Bind / CarrierNotification）且号对上由设备认定，没预告的等 Host ProceedWithCarrier；
///    认定后自动 Load（夹紧、开门、读槽图）；槽图跟 Host 给的一样由设备认定，否则等 Host；认定了就是"料到了"（E90 建片对象）；
///    Host 取消（CancelCarrier）的、核对不过的卸下来等取走；干完（CJ 完成）自动 Unload。
/// ② 端口：搬运状态（停用 / 挡着 / 等送 / 等取，也是 E84 交接的许可）、存取方式（手动 / 自动）、关联、预约。
/// ③ 报文：S3F17 载具动作、S3F25 端口动作、S3F27 改存取方式；E39 对象 Carrier、Port。每个状态转换报一个事件。
/// 设备侧的事实（在位、读码、槽图、Load / Unload）由 LoadPort 经 IE87Callback 报过来，Host 的决定经 ILoadPort 写回（认定的载具号、核对状态）。
/// </summary>
[Component(description: "载具管理（SEMI E87）：载具 ID / 槽图跟 Host 核对、端口搬运状态、预约和绑定，S3 报文")]
public partial class E87Component : ComponentBase, IE87Callback, IE84Provider
{
    #region DV 代码（事件带的数据）

    private const string DvCarrierId = "CarrierID";
    private const string DvPortId = "PortID";
    private const string DvCarrierIdStatus = "CarrierIDStatus";
    private const string DvSlotMapStatus = "SlotMapStatus";
    private const string DvAccessingStatus = "CarrierAccessingStatus";
    private const string DvCapacity = "Capacity";
    private const string DvSubstrateCount = "SubstrateCount";
    private const string DvSlotMap = "SlotMap";
    private const string DvContentMap = "ContentMap";
    private const string DvUsage = "Usage";
    private const string DvLocationId = "LocationID";
    private const string DvTransferState = "PortTransferState";
    private const string DvAccessMode = "PortAccessMode";
    private const string DvAssociationState = "PortAssociationState";
    private const string DvReservationState = "PortReservationState";
    private const string DvReason = "Reason";

    [DataVariable(ValueFormat.String, "载具号")]
    public readonly string CarrierIdData = DvCarrierId;

    [DataVariable(ValueFormat.Int, "端口号（PortID，按 LoadPort 先后从 1 开始）")]
    public readonly string PortIdData = DvPortId;

    [DataVariable(ValueFormat.Int, "载具 ID 状态：0 没读、1 等 Host、2 认定、3 核对不过")]
    public readonly string CarrierIdStatusData = DvCarrierIdStatus;

    [DataVariable(ValueFormat.Int, "槽图状态：0 没读、1 等 Host、2 认定、3 核对不过")]
    public readonly string SlotMapStatusData = DvSlotMapStatus;

    [DataVariable(ValueFormat.Int, "载具取放状态：0 没取放、1 在取放、2 干完、3 中断")]
    public readonly string AccessingStatusData = DvAccessingStatus;

    [DataVariable(ValueFormat.Int, "载具槽数")]
    public readonly string CapacityData = DvCapacity;

    [DataVariable(ValueFormat.Int, "载具里的片数")]
    public readonly string SubstrateCountData = DvSubstrateCount;

    [DataVariable(ValueFormat.String, "槽图：L[槽数]{U1}，1 空、2 有片说不准、3 正常、4 叠片、5 交叉")]
    public readonly string SlotMapData = DvSlotMap;

    [DataVariable(ValueFormat.String, "片号表：L[槽数]{L[2]{批次号, 片号}}")]
    public readonly string ContentMapData = DvContentMap;

    [DataVariable(ValueFormat.String, "载具用途（Host 给的）")]
    public readonly string UsageData = DvUsage;

    [DataVariable(ValueFormat.String, "载具在哪（端口名）")]
    public readonly string LocationIdData = DvLocationId;

    [DataVariable(ValueFormat.Int, "端口搬运状态：0 停用、1 挡着、2 等送、3 等取")]
    public readonly string TransferStateData = DvTransferState;

    [DataVariable(ValueFormat.Int, "端口存取方式：0 手动、1 自动")]
    public readonly string AccessModeData = DvAccessMode;

    [DataVariable(ValueFormat.Int, "端口关联：0 没关联、1 关联了载具")]
    public readonly string AssociationStateData = DvAssociationState;

    [DataVariable(ValueFormat.Int, "端口预约：0 没预约、1 预约了")]
    public readonly string ReservationStateData = DvReservationState;

    [DataVariable(ValueFormat.Int, "原因：0 等 Host 核对槽图、1 槽图核对不过、2 读码失败、5 Host 取消、6 载具号重了")]
    public readonly string ReasonData = DvReason;

    #endregion

    #region 事件：载具（E87 Carrier 状态机 #1~#21）

    [EventAttribut("载具对象建了（#1）", Data = new[] { DvCarrierId, DvLocationId })]
    public readonly string CarrierTrans01 = "CarrierSMTrans01";

    [EventAttribut("载具 ID 没读（#2，Host 预告的载具）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvCapacity, DvSubstrateCount, DvUsage })]
    public readonly string CarrierTrans02 = "CarrierSMTrans02";

    [EventAttribut("载具 ID 等 Host 核对（#3）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId })]
    public readonly string CarrierTrans03 = "CarrierSMTrans03";

    [EventAttribut("载具 ID 认定（#4，Host 给号）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId })]
    public readonly string CarrierTrans04 = "CarrierSMTrans04";

    [EventAttribut("载具 ID 核对不过（#5）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId, DvReason })]
    public readonly string CarrierTrans05 = "CarrierSMTrans05";

    [EventAttribut("载具 ID 认定（#6，号对上预告）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId })]
    public readonly string CarrierTrans06 = "CarrierSMTrans06";

    [EventAttribut("载具 ID 等 Host（#7，预告的载具读码失败）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId, DvReason })]
    public readonly string CarrierTrans07 = "CarrierSMTrans07";

    [EventAttribut("载具 ID 认定（#8，Host 让继续）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId })]
    public readonly string CarrierTrans08 = "CarrierSMTrans08";

    [EventAttribut("载具 ID 核对不过（#9，Host 取消）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId, DvReason })]
    public readonly string CarrierTrans09 = "CarrierSMTrans09";

    [EventAttribut("载具槽图没读（#12）", Data = new[] { DvCarrierId, DvSlotMapStatus })]
    public readonly string CarrierTrans12 = "CarrierSMTrans12";

    [EventAttribut("载具槽图认定（#13，跟 Host 给的一样）", Data = new[] { DvCarrierId, DvSlotMapStatus, DvSlotMap, DvCapacity, DvSubstrateCount, DvLocationId, DvPortId })]
    public readonly string CarrierTrans13 = "CarrierSMTrans13";

    [EventAttribut("载具槽图等 Host 核对（#14）", Data = new[] { DvCarrierId, DvSlotMapStatus, DvSlotMap, DvCapacity, DvSubstrateCount, DvLocationId, DvPortId, DvReason })]
    public readonly string CarrierTrans14 = "CarrierSMTrans14";

    [EventAttribut("载具槽图认定（#15，Host 让继续）", Data = new[] { DvCarrierId, DvSlotMapStatus, DvSlotMap, DvContentMap, DvLocationId, DvPortId })]
    public readonly string CarrierTrans15 = "CarrierSMTrans15";

    [EventAttribut("载具槽图核对不过（#16，Host 取消）", Data = new[] { DvCarrierId, DvSlotMapStatus, DvLocationId, DvPortId, DvReason })]
    public readonly string CarrierTrans16 = "CarrierSMTrans16";

    [EventAttribut("载具没取放（#17）", Data = new[] { DvCarrierId, DvAccessingStatus })]
    public readonly string CarrierTrans17 = "CarrierSMTrans17";

    [EventAttribut("载具开始取放（#18）", Data = new[] { DvCarrierId, DvAccessingStatus, DvLocationId, DvPortId })]
    public readonly string CarrierTrans18 = "CarrierSMTrans18";

    [EventAttribut("载具干完（#19）", Data = new[] { DvCarrierId, DvAccessingStatus, DvContentMap, DvLocationId, DvPortId })]
    public readonly string CarrierTrans19 = "CarrierSMTrans19";

    [EventAttribut("载具中断（#20）", Data = new[] { DvCarrierId, DvAccessingStatus, DvLocationId, DvPortId })]
    public readonly string CarrierTrans20 = "CarrierSMTrans20";

    [EventAttribut("载具对象删了（#21：取走、取消预告）", Data = new[] { DvCarrierId, DvLocationId, DvPortId })]
    public readonly string CarrierTrans21 = "CarrierSMTrans21";

    #endregion

    #region 事件：端口搬运状态（E87 Load Port Transfer 状态机 #1~#10）

    [EventAttribut("端口搬运状态初始（#1）", Data = new[] { DvPortId, DvTransferState })]
    public readonly string PortTrans01 = "PortTransferSMTrans01";

    [EventAttribut("端口启用（#2）", Data = new[] { DvPortId, DvTransferState })]
    public readonly string PortTrans02 = "PortTransferSMTrans02";

    [EventAttribut("端口停用（#3）", Data = new[] { DvPortId, DvTransferState })]
    public readonly string PortTrans03 = "PortTransferSMTrans03";

    [EventAttribut("端口启用后进挡着 / 可交接（#4）", Data = new[] { DvPortId, DvTransferState })]
    public readonly string PortTrans04 = "PortTransferSMTrans04";

    [EventAttribut("端口可交接后进等送 / 等取（#5）", Data = new[] { DvPortId, DvTransferState, DvCarrierId })]
    public readonly string PortTrans05 = "PortTransferSMTrans05";

    [EventAttribut("端口等送 → 挡着（#6，载具放上来）", Data = new[] { DvPortId, DvTransferState })]
    public readonly string PortTrans06 = "PortTransferSMTrans06";

    [EventAttribut("端口等取 → 挡着（#7，开始取走）", Data = new[] { DvPortId, DvTransferState })]
    public readonly string PortTrans07 = "PortTransferSMTrans07";

    [EventAttribut("端口挡着 → 等送（#8，载具取走了）", Data = new[] { DvPortId, DvTransferState })]
    public readonly string PortTrans08 = "PortTransferSMTrans08";

    [EventAttribut("端口挡着 → 等取（#9，载具卸好了）", Data = new[] { DvPortId, DvTransferState, DvCarrierId })]
    public readonly string PortTrans09 = "PortTransferSMTrans09";

    [EventAttribut("端口挡着 → 可交接（#10）", Data = new[] { DvPortId, DvTransferState })]
    public readonly string PortTrans10 = "PortTransferSMTrans10";

    #endregion

    #region 事件：存取方式、关联、预约、其它

    [EventAttribut("端口转自动存取（AMHS）", Data = new[] { DvPortId, DvAccessMode })]
    public readonly string AccessGoAuto = "AccessSMGoAuto";

    [EventAttribut("端口转手动存取", Data = new[] { DvPortId, DvAccessMode })]
    public readonly string AccessGoManual = "AccessSMGoManual";

    [EventAttribut("端口关联了载具", Data = new[] { DvPortId, DvAssociationState, DvCarrierId })]
    public readonly string AssociationGo = "AssocSMGoAssoc";

    [EventAttribut("端口取消关联", Data = new[] { DvPortId, DvAssociationState })]
    public readonly string AssociationGoNot = "AssocSMGoNotAssoc";

    [EventAttribut("端口预约了", Data = new[] { DvPortId, DvReservationState, DvCarrierId })]
    public readonly string ReservationGo = "ReservationSMGoReserved";

    [EventAttribut("端口取消预约", Data = new[] { DvPortId, DvReservationState })]
    public readonly string ReservationGoNot = "ReservationSMGoNotReserved";

    [EventAttribut("读码失败（没有预告，等 Host 给号）", Data = new[] { DvPortId })]
    public readonly string CarrierIdReadFailEvent = "CarrierIDReadFail";

    [EventAttribut("载具号跟机内另一个载具重了", Data = new[] { DvCarrierId, DvPortId })]
    public readonly string DuplicateCarrierIdEvent = "DuplicateCarrierID";

    [EventAttribut("载具夹紧", Data = new[] { DvCarrierId, DvPortId })]
    public readonly string CarrierClampedEvent = "CarrierClamped";

    [EventAttribut("载具松开", Data = new[] { DvCarrierId, DvPortId })]
    public readonly string CarrierUnclampedEvent = "CarrierUnclamped";

    [EventAttribut("载具门打开（Load 好了）", Data = new[] { DvCarrierId, DvPortId })]
    public readonly string CarrierOpenedEvent = "CarrierOpened";

    [EventAttribut("载具门关上（Unload 好了）", Data = new[] { DvCarrierId, DvPortId })]
    public readonly string CarrierClosedEvent = "CarrierClosed";

    [EventAttribut("载具放到端口上（GEM Material Received）", Data = new[] { DvPortId })]
    public readonly string MaterialReceivedEvent = "MaterialReceived";

    [EventAttribut("载具从端口拿走（GEM Material Removed）", Data = new[] { DvPortId, DvCarrierId })]
    public readonly string MaterialRemovedEvent = "MaterialRemoved";

    #endregion

    #region SC / EC

    [SCEditor("True", "E87", "载具 ID 认定后自动 Load（夹紧、开门、读槽图）；False = 等操作员点 Load")]
    public bool AutoLoad { get; set; } = true;

    [SCEditor("True", "E87", "载具干完、中断、被取消或核对不过后自动 Unload（关门、松开），端口转等取；False = 等操作员点 Unload")]
    public bool AutoUnload { get; set; } = true;

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "10000", @default: "500",
        description: "多久查一次端口状态：端口忙闲、报警这些没有回调，搬运状态的变化靠它发现")]
    public int PortPollMs
    {
        get { return GetEcInt(nameof(PortPollMs)); }
        set { SetEcInt(nameof(PortPollMs), value); }
    }

    #endregion

    #region SV

    /// <summary>各端口搬运状态（按 PortID 先后）。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "各端口搬运状态（L{U1}，0 停用、1 挡着、2 等送、3 等取）")]
    public SecsItem PortTransferStateList => PortList(port => port.TransferState ?? E87Codes.TransferBlocked);

    /// <summary>各端口存取方式。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "各端口存取方式（L{U1}，0 手动、1 自动）")]
    public SecsItem PortAccessModeList => PortList(port => port.AccessMode);

    /// <summary>各端口关联状态。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "各端口关联状态（L{U1}，0 没关联、1 关联了）")]
    public SecsItem PortAssociationStateList => PortList(port => port.AssociationState);

    /// <summary>各端口预约状态。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "各端口预约状态（L{U1}，0 没预约、1 预约了）")]
    public SecsItem PortReservationStateList => PortList(port => port.ReservationState);

    /// <summary>载具在哪（E87 CarrierLocationMatrix）：只列已经到了、认出号的。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "载具在哪（L{L[2]{位置, 载具号}}）")]
    public SecsItem CarrierLocationMatrix
    {
        get
        {
            lock (_gate)
            {
                return SecsItem.L(_ports.Select(port => SecsItem.L(SecsItem.A(GemValue.Ascii(port.Device.Name)),
                    SecsItem.A(port.Carrier is not null && port.Carrier.Arrived ? GemValue.Ascii(port.Carrier.Id) : string.Empty))));
            }
        }
    }

    private SecsItem PortList(Func<E87Port, byte> value)
    {
        lock (_gate)
        {
            return SecsItem.L(_ports.Select(port => SecsItem.U1(value(port))));
        }
    }

    #endregion

    private readonly object _gate = new();
    private readonly List<E87Port> _ports = [];
    private readonly Dictionary<string, E87Carrier> _carriers = new(StringComparer.OrdinalIgnoreCase);
    private E30Component? _gem;
    private Action<string>? _materialVerified;
    private Timer? _poll;

    #region 接设备

    /// <summary>
    /// 接到链路和设备上（EAP 组件在链路打开之前调）：端口按给的先后编 PortID，挂上 E87 回调和 E84 反查口，
    /// 登记 S3 处理方和 E39 对象类型；materialVerified 是槽图认定（料到了）时通知的（E90 据此建片对象）。
    /// 开始按 EC PortPollMs 查端口状态。
    /// </summary>
    public void Attach(HsmsComponent link, E30Component gem, E39Component? objects, IReadOnlyList<ILoadPort> ports,
        Action<string>? materialVerified)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        _gem = gem;
        _materialVerified = materialVerified;
        lock (_gate)
        {
            byte id = 1;
            foreach (var device in ports)
            {
                var port = new E87Port { Device = device, Id = id++, Present = device.IsPodPlaced };
                _ports.Add(port);
                device.E87Callback = this;
                device.E84Provider = this;
            }

            foreach (var port in _ports)
            {
                RefreshTransferState(port);
            }
        }

        link.Handle(3, 15, Inquire);
        link.Handle(3, 17, CarrierAction);
        link.Handle(3, 25, PortAction);
        link.Handle(3, 27, ChangeAccess);
        objects?.Register(new E87CarrierType(this));
        objects?.Register(new E87PortType(this));
        _poll = new Timer(_ => Poll(), null, Math.Max(100, PortPollMs), Math.Max(100, PortPollMs));
        LogHelper.Info(Name, $"E87 接上 {_ports.Count} 个端口：{string.Join("、", _ports.Select(port => $"{port.Id}={port.Device.Name}"))}");
    }

    /// <summary>从设备上摘下来（宿主退出时）。</summary>
    public void Detach()
    {
        _poll?.Dispose();
        _poll = null;
        lock (_gate)
        {
            foreach (var port in _ports)
            {
                port.Device.E87Callback = null;
                port.Device.E84Provider = null;
            }
        }
    }

    private void Poll()
    {
        try
        {
            lock (_gate)
            {
                foreach (var port in _ports)
                {
                    RefreshTransferState(port);
                }
            }
        }
        catch (Exception exception)
        {
            LogHelper.Warn(Name, $"查端口状态出错：{exception.Message}");
        }
    }

    private E87Port? PortOf(ILoadPort device)
    {
        return _ports.FirstOrDefault(port => ReferenceEquals(port.Device, device));
    }

    #endregion

    #region 设备回调（IE87Callback，在各 LoadPort 的 EAP 派发线程上）

    void IE87Callback.CarrierArrived(ILoadPort device)
    {
        lock (_gate)
        {
            var port = PortOf(device);
            if (port is null)
            {
                return;
            }

            port.Present = true;
            port.ReadFailed = false;
            port.Rejected = false;
            if (port.Reserved)
            {
                port.Reserved = false;
                ReportPort(ReservationGoNot, port);
            }

            var bound = port.Carrier;
            if (bound is not null)
            {
                bound.Arrived = true;
            }

            ReportPort(MaterialReceivedEvent, port);
            RefreshTransferState(port);
        }
    }

    void IE87Callback.CarrierIdRead(ILoadPort device, string carrierId)
    {
        var actions = new List<Action>();
        lock (_gate)
        {
            var port = PortOf(device);
            if (port is not null)
            {
                IdRead(port, carrierId, actions);
            }
        }

        Run(actions);
    }

    /// <summary>读到载具号：对上预告的设备认定；没预告的建对象等 Host；跟机内的重了这一盒不收。</summary>
    private void IdRead(E87Port port, string carrierId, List<Action> actions)
    {
        var device = port.Device;
        var bound = port.Carrier;
        if (bound is not null && bound.IdStatus == E87Codes.IdNotRead)
        {
            if (string.Equals(bound.Id, carrierId, StringComparison.OrdinalIgnoreCase))
            {
                // 号对上 Bind 的预告：设备认定（#6）
                Verify(port, bound, CarrierTrans06, actions);
                return;
            }

            // 来的不是 Bind 预告的那个：预告作废（#21），按没预告的处理
            LogHelper.Warn(Name, $"端口 {port.Id} 绑定的是 {bound.Id}，来的是 {carrierId}：绑定作废");
            Remove(bound);
        }

        if (_carriers.TryGetValue(carrierId, out var existing))
        {
            if (existing.Port is null)
            {
                // CarrierNotification 预告过的号：关联上端口，设备认定（#6）
                existing.Port = port;
                existing.Arrived = true;
                port.Carrier = existing;
                ReportAssociation(port);
                Verify(port, existing, CarrierTrans06, actions);
                return;
            }

            // 号跟机内另一个载具重了：这一盒不要，卸下来等取走
            LogHelper.Warn(Name, $"端口 {port.Id} 读到的载具号 {carrierId} 跟端口 {existing.Port.Id} 上的重了，这一盒不收");
            port.Rejected = true;
            Report(DuplicateCarrierIdEvent, new GemData(DvCarrierId, carrierId), new GemData(DvPortId, port.Id));
            actions.Add(() => device.UpdateCarrierStatus(CarrierIdStatus.VerifyFailed, null));
            RefreshTransferState(port);
            return;
        }

        // 没预告：建载具对象（#1、#3、#12、#17），关联端口，等 Host 核对
        var carrier = new E87Carrier
        {
            Id = carrierId,
            Port = port,
            Arrived = true,
            IdStatus = E87Codes.IdWaitingForHost,
            Capacity = (byte)Math.Min(byte.MaxValue, device.SlotCount),
        };
        _carriers[carrierId] = carrier;
        port.Carrier = carrier;
        ReportCarrier(CarrierTrans01, carrier, port);
        ReportCarrier(CarrierTrans03, carrier, port);
        ReportCarrier(CarrierTrans12, carrier, port);
        ReportCarrier(CarrierTrans17, carrier, port);
        ReportAssociation(port);
        actions.Add(() => device.UpdateCarrierStatus(CarrierIdStatus.WaitingForHost, null));
    }

    void IE87Callback.CarrierIdReadFailed(ILoadPort device)
    {
        var actions = new List<Action>();
        lock (_gate)
        {
            var port = PortOf(device);
            if (port is null)
            {
                return;
            }

            var bound = port.Carrier;
            if (bound is not null && bound.IdStatus == E87Codes.IdNotRead)
            {
                // Bind 预告的载具读码失败：等 Host（#7，原因 2）
                bound.IdStatus = E87Codes.IdWaitingForHost;
                ReportCarrier(CarrierTrans07, bound, port, E87Codes.ReasonReadFail);
                actions.Add(() => device.UpdateCarrierStatus(CarrierIdStatus.WaitingForHost, null));
            }
            else if (bound is null)
            {
                // 没预告又读不出号：没有载具对象，报读码失败，等 Host 带端口号给 ID（ProceedWithCarrier）或取消
                port.ReadFailed = true;
                ReportPort(CarrierIdReadFailEvent, port);
            }
        }

        Run(actions);
    }

    void IE87Callback.SlotMapRead(ILoadPort device, IReadOnlyList<SlotState> slotMap)
    {
        var actions = new List<Action>();
        lock (_gate)
        {
            var port = PortOf(device);
            var carrier = port?.Carrier;
            if (port is not null && carrier is not null && carrier.SlotMapStatus == E87Codes.MapNotRead)
            {
                SlotMapRead(port, carrier, slotMap, actions);
            }
        }

        Run(actions);
    }

    /// <summary>读到槽图：跟 Host 给的一样由设备认定（#13，料到了），否则等 Host 核对（#14）。</summary>
    private void SlotMapRead(E87Port port, E87Carrier carrier, IReadOnlyList<SlotState> slotMap, List<Action> actions)
    {
        var codes = slotMap.Select(slot => (byte)slot).ToArray();
        carrier.SlotMap = codes;
        carrier.Capacity = (byte)Math.Min(byte.MaxValue, codes.Length);
        carrier.SubstrateCount = (byte)codes.Count(code => code is E87Codes.SlotCorrectlyOccupied or E87Codes.SlotNotEmpty);
        var expected = carrier.ExpectedSlotMap;
        if (expected is not null && expected.SequenceEqual(codes))
        {
            MaterialVerified(port, carrier, CarrierTrans13, actions);
            return;
        }

        carrier.SlotMapStatus = E87Codes.MapWaitingForHost;
        ReportCarrier(CarrierTrans14, carrier, port,
            expected is null ? E87Codes.ReasonVerificationNeeded : E87Codes.ReasonVerificationUnsuccessful);
        var device = port.Device;
        actions.Add(() => device.UpdateCarrierStatus(null, CarrierSlotMapStatus.WaitingForHost));
    }

    void IE87Callback.LoadCompleted(ILoadPort device)
    {
        PortEvent(device, CarrierOpenedEvent);
    }

    void IE87Callback.UnloadCompleted(ILoadPort device)
    {
        PortEvent(device, CarrierClosedEvent);
    }

    void IE87Callback.Homed(ILoadPort device)
    {
        PortEvent(device, null);
    }

    void IE87Callback.ClampCompleted(ILoadPort device)
    {
        PortEvent(device, CarrierClampedEvent);
    }

    void IE87Callback.UnclampCompleted(ILoadPort device)
    {
        PortEvent(device, CarrierUnclampedEvent);
    }

    void IE87Callback.AutoModeChanged(ILoadPort device, bool autoMode)
    {
        lock (_gate)
        {
            var port = PortOf(device);
            if (port is null)
            {
                return;
            }

            ReportPort(autoMode ? AccessGoAuto : AccessGoManual, port);
            RefreshTransferState(port);
        }
    }

    void IE87Callback.AccessStarted(ILoadPort device)
    {
        AccessChanged(device, E87Codes.NotAccessed, E87Codes.InAccess, CarrierTrans18);
    }

    void IE87Callback.AccessStopped(ILoadPort device)
    {
        // Unload 了：取放过、没干完的算中断（#20）；Unload 好了能不能取走看搬运状态
        AccessChanged(device, E87Codes.InAccess, E87Codes.CarrierStopped, CarrierTrans20);
    }

    void IE87Callback.CarrierComplete(ILoadPort device)
    {
        var actions = new List<Action>();
        lock (_gate)
        {
            var port = PortOf(device);
            var carrier = port?.Carrier;
            if (port is null || carrier is null)
            {
                return;
            }

            if (carrier.Accessing == E87Codes.NotAccessed)
            {
                carrier.Accessing = E87Codes.InAccess;
                ReportCarrier(CarrierTrans18, carrier, port);
            }

            if (carrier.Accessing == E87Codes.InAccess)
            {
                carrier.Accessing = E87Codes.CarrierComplete;
                ReportCarrier(CarrierTrans19, carrier, port);
            }

            UnloadIfDone(port, actions);
        }

        Run(actions);
    }

    void IE87Callback.PortError(ILoadPort device, string error)
    {
        // 取放途中出错：设备侧已经把载具记成中断，这边跟着报 #20
        AccessChanged(device, E87Codes.InAccess, E87Codes.CarrierStopped, CarrierTrans20);
    }

    void IE87Callback.CarrierRemoved(ILoadPort device, string? carrierId)
    {
        lock (_gate)
        {
            var port = PortOf(device);
            if (port is null)
            {
                return;
            }

            port.Present = false;
            port.ReadFailed = false;
            port.Rejected = false;
            var carrier = port.Carrier;
            string removedId = carrier?.Id ?? carrierId ?? string.Empty;
            if (carrier is not null)
            {
                Remove(carrier);
            }

            Report(MaterialRemovedEvent, new GemData(DvPortId, port.Id), new GemData(DvCarrierId, GemValue.Ascii(removedId)));
            RefreshTransferState(port);
        }
    }

    private void PortEvent(ILoadPort device, string? code)
    {
        lock (_gate)
        {
            var port = PortOf(device);
            if (port is null)
            {
                return;
            }

            if (code is not null)
            {
                Report(code, new GemData(DvCarrierId, GemValue.Ascii(port.Carrier?.Id ?? device.CarrierId ?? string.Empty)),
                    new GemData(DvPortId, port.Id));
            }

            RefreshTransferState(port);
        }
    }

    private void AccessChanged(ILoadPort device, byte from, byte to, string code)
    {
        var actions = new List<Action>();
        lock (_gate)
        {
            var port = PortOf(device);
            var carrier = port?.Carrier;
            if (port is null || carrier is null || carrier.Accessing != from)
            {
                return;
            }

            carrier.Accessing = to;
            ReportCarrier(code, carrier, port);
            if (to == E87Codes.CarrierStopped)
            {
                UnloadIfDone(port, actions);
            }

            RefreshTransferState(port);
        }

        Run(actions);
    }

    #endregion

    #region 核对、收尾（都在锁里调，设备动作攒到锁外做）

    /// <summary>载具 ID 认定（#4 / #6 / #8）：写回设备（认定的号），Load 起来读槽图。</summary>
    private void Verify(E87Port port, E87Carrier carrier, string code, List<Action> actions)
    {
        carrier.IdStatus = E87Codes.IdVerified;
        ReportCarrier(code, carrier, port);
        var device = port.Device;
        string id = carrier.Id;
        actions.Add(() => device.SetCarrierId(id));
        if (AutoLoad)
        {
            actions.Add(() =>
            {
                if (device.IsIdle && device.IsPodPlaced && device.Load() is null)
                {
                    LogHelper.Warn(Name, $"{device.Name} 载具 {id} 认定了，但现在 Load 不了（端口状态不允许），等操作员处理");
                }
            });
        }
    }

    /// <summary>
    /// 槽图认定（#13 / #15）：料到了。Host 给了片号表的写进晶圆账（片号、批次号），再通知 E90 建片对象。
    /// </summary>
    private void MaterialVerified(E87Port port, E87Carrier carrier, string code, List<Action> actions)
    {
        carrier.SlotMapStatus = E87Codes.MapVerified;
        ReportCarrier(code, carrier, port);
        var device = port.Device;
        var content = carrier.ContentMap?.ToList();
        actions.Add(() =>
        {
            device.UpdateCarrierStatus(null, CarrierSlotMapStatus.Verified);
            var ledger = WaferManager.Current;
            if (ledger is not null && content is not null)
            {
                for (int index = 0; index < content.Count; index++)
                {
                    var (lotId, substrateId) = content[index];
                    if (ledger.Get(device.Name, index + 1) is null)
                    {
                        continue;
                    }

                    if (substrateId.Length > 0)
                    {
                        ledger.SetWaferId(device.Name, index + 1, substrateId);
                    }

                    if (lotId.Length > 0)
                    {
                        ledger.SetLotId(device.Name, index + 1, lotId);
                    }
                }
            }

            _materialVerified?.Invoke(device.Name);
        });
    }

    /// <summary>
    /// 这一盒不要了（Host 取消、核对不过）：Load 着的卸下来，端口转等取走。
    /// </summary>
    private void Reject(E87Port port, List<Action> actions)
    {
        port.Rejected = true;
        var device = port.Device;
        if (AutoUnload)
        {
            actions.Add(() =>
            {
                if (device.IsLoaded && device.Unload() is null)
                {
                    LogHelper.Warn(Name, $"{device.Name} 载具不要了，但现在 Unload 不了（端口状态不允许），等操作员处理");
                }
            });
        }

        RefreshTransferState(port);
    }

    /// <summary>干完、中断了：Load 着的卸下来（AutoUnload 开着时）。</summary>
    private void UnloadIfDone(E87Port port, List<Action> actions)
    {
        if (!AutoUnload)
        {
            return;
        }

        var device = port.Device;
        actions.Add(() =>
        {
            if (device.IsLoaded && device.Unload() is null)
            {
                LogHelper.Warn(Name, $"{device.Name} 载具干完了，但现在 Unload 不了（还在被机械手服务或端口状态不允许），等操作员处理");
            }
        });
    }

    /// <summary>删载具对象（#21），摘掉关联、预约。</summary>
    private void Remove(E87Carrier carrier)
    {
        var port = carrier.Port;
        ReportCarrier(CarrierTrans21, carrier, port);
        _carriers.Remove(carrier.Id);
        if (port is not null && ReferenceEquals(port.Carrier, carrier))
        {
            port.Carrier = null;
            ReportPort(AssociationGoNot, port);
            if (carrier.Bound && port.Reserved)
            {
                port.Reserved = false;
                ReportPort(ReservationGoNot, port);
            }
        }
    }

    /// <summary>设备动作在锁外做：动作发起会拿模块的锁，别跟这边的锁绞在一起。</summary>
    private void Run(List<Action> actions)
    {
        foreach (var action in actions)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                LogHelper.Warn(Name, $"E87 设备动作出错：{exception.Message}");
            }
        }
    }

    #endregion

    #region 端口搬运状态（也是 E84 的许可）

    /// <summary>
    /// 端口在 Host 眼里的搬运状态：Host 停用 → 停用；设备说等送 / 等取就是；空闲、有载具、这一盒不要了 → 等取；别的都是挡着
    /// （设备没初始化、出错这类在 E87 里也算挡着——停用只由 Host 说了算）。
    /// </summary>
    private static byte ComputeTransferState(E87Port port)
    {
        if (!port.InService)
        {
            return E87Codes.OutOfService;
        }

        var local = port.Device.LocalTransferState;
        if (local == LoadPortTransferState.ReadyToLoad)
        {
            return E87Codes.ReadyToLoad;
        }

        if (local == LoadPortTransferState.ReadyToUnload)
        {
            return E87Codes.ReadyToUnload;
        }

        return port.Rejected && port.Device.IsIdle && port.Device.IsPodPlaced ? E87Codes.ReadyToUnload : E87Codes.TransferBlocked;
    }

    /// <summary>
    /// 重算一个端口的搬运状态，变了按 E87 的转换报事件：停用 ↔ 启用（#2 / #3，启用后进挡着或可交接 #4、#5），
    /// 等送 / 等取 → 挡着（#6 / #7），挡着 → 等送 / 等取（#8 / #9），等送、等取直接互换的中间补一个挡着。第一次报 #1。
    /// </summary>
    private void RefreshTransferState(E87Port port)
    {
        byte next = ComputeTransferState(port);
        byte? last = port.TransferState;
        if (last == next)
        {
            return;
        }

        port.TransferState = next;
        if (last is null)
        {
            ReportPort(PortTrans01, port);
            return;
        }

        if (next == E87Codes.OutOfService)
        {
            ReportPort(PortTrans03, port);
            return;
        }

        if (last == E87Codes.OutOfService)
        {
            ReportPort(PortTrans02, port);
            ReportPort(PortTrans04, port);
            if (next != E87Codes.TransferBlocked)
            {
                ReportPort(PortTrans05, port);
            }

            return;
        }

        if (next == E87Codes.TransferBlocked)
        {
            ReportPort(last == E87Codes.ReadyToLoad ? PortTrans06 : PortTrans07, port);
            return;
        }

        if (last != E87Codes.TransferBlocked)
        {
            ReportPort(last == E87Codes.ReadyToLoad ? PortTrans06 : PortTrans07, port);
        }

        ReportPort(next == E87Codes.ReadyToLoad ? PortTrans08 : PortTrans09, port);
    }

    LoadPortTransferState IE84Provider.GetTransferState(ILoadPort device)
    {
        lock (_gate)
        {
            var port = PortOf(device);
            if (port is null)
            {
                return device.LocalTransferState;
            }

            if (!port.InService || device.LocalTransferState == LoadPortTransferState.OutOfService)
            {
                return LoadPortTransferState.OutOfService;
            }

            return ComputeTransferState(port) switch
            {
                E87Codes.ReadyToLoad => LoadPortTransferState.ReadyToLoad,
                E87Codes.ReadyToUnload => LoadPortTransferState.ReadyToUnload,
                _ => LoadPortTransferState.TransferBlocked,
            };
        }
    }

    bool IE84Provider.IsAutoAccessMode(ILoadPort device)
    {
        return device.IsAutoMode;
    }

    #endregion

    #region 报事件

    private void ReportAssociation(E87Port port)
    {
        Report(AssociationGo, PortData(port).Append(new GemData(DvCarrierId, GemValue.Ascii(port.Carrier?.Id ?? string.Empty))).ToArray());
    }

    private void ReportPort(string code, E87Port port)
    {
        Report(code, PortData(port).Append(new GemData(DvCarrierId, GemValue.Ascii(port.Carrier?.Id ?? string.Empty))).ToArray());
    }

    private void ReportCarrier(string code, E87Carrier carrier, E87Port? port, byte? reason = null)
    {
        var data = CarrierData(carrier).ToList();
        if (port is not null)
        {
            data.AddRange(PortData(port));
        }

        if (reason is not null)
        {
            data.Add(new GemData(DvReason, reason.Value));
        }

        Report(code, data.ToArray());
    }

    private void Report(string code, params GemData[] data)
    {
        _gem?.Report(this, code, data);
    }

    private static IEnumerable<GemData> PortData(E87Port port)
    {
        yield return new GemData(DvPortId, port.Id);
        yield return new GemData(DvTransferState, port.TransferState ?? E87Codes.TransferBlocked);
        yield return new GemData(DvAccessMode, port.AccessMode);
        yield return new GemData(DvAssociationState, port.AssociationState);
        yield return new GemData(DvReservationState, port.ReservationState);
    }

    private static IEnumerable<GemData> CarrierData(E87Carrier carrier)
    {
        yield return new GemData(DvCarrierId, GemValue.Ascii(carrier.Id));
        yield return new GemData(DvCarrierIdStatus, carrier.IdStatus);
        yield return new GemData(DvSlotMapStatus, carrier.SlotMapStatus);
        yield return new GemData(DvAccessingStatus, carrier.Accessing);
        yield return new GemData(DvCapacity, carrier.Capacity is null ? SecsItem.U1() : SecsItem.U1(carrier.Capacity.Value));
        yield return new GemData(DvSubstrateCount, carrier.SubstrateCount is null ? SecsItem.U1() : SecsItem.U1(carrier.SubstrateCount.Value));
        yield return new GemData(DvSlotMap, SlotMapItem(carrier));
        yield return new GemData(DvContentMap, ContentMapItem(carrier));
        yield return new GemData(DvUsage, GemValue.Ascii(carrier.Usage));
        yield return new GemData(DvLocationId, GemValue.Ascii(carrier.LocationId));
    }

    /// <summary>槽图 L[槽数]{U1}：读到了报读到的，没读报 Host 给的，都没有报空表。</summary>
    private static SecsItem SlotMapItem(E87Carrier carrier)
    {
        var map = carrier.SlotMap ?? carrier.ExpectedSlotMap ?? [];
        return SecsItem.L(map.Select(code => SecsItem.U1(code)));
    }

    /// <summary>
    /// 片号表 L[槽数]{L[2]{批次号, 片号}}：槽图认定后按晶圆账报（片号、批次号以账为准）；没认定报 Host 给的；都没有报空表。
    /// </summary>
    private static SecsItem ContentMapItem(E87Carrier carrier)
    {
        var port = carrier.Port;
        var ledger = WaferManager.Current;
        if (carrier.SlotMapStatus == E87Codes.MapVerified && port is not null && ledger is not null)
        {
            return SecsItem.L(ledger.GetSlots(port.Device.Name).Select(wafer => SecsItem.L(
                SecsItem.A(GemValue.Ascii(wafer?.LotId ?? string.Empty)), SecsItem.A(GemValue.Ascii(wafer?.WaferId ?? string.Empty)))));
        }

        return SecsItem.L((carrier.ContentMap ?? []).Select(entry =>
            SecsItem.L(SecsItem.A(GemValue.Ascii(entry.LotId)), SecsItem.A(GemValue.Ascii(entry.SubstrateId)))));
    }

    #endregion

    #region E39 对象

    internal IReadOnlyList<string> CarrierIds()
    {
        lock (_gate)
        {
            return _carriers.Keys.ToList();
        }
    }

    internal IReadOnlyList<string> PortIds()
    {
        lock (_gate)
        {
            return _ports.Select(port => port.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        }
    }

    internal bool TryGetCarrierAttribute(string carrierId, string attribute, out SecsItem value)
    {
        lock (_gate)
        {
            value = SecsItem.L();
            if (!_carriers.TryGetValue(carrierId, out var carrier))
            {
                return false;
            }

            value = attribute switch
            {
                "ObjType" => SecsItem.A("Carrier"),
                "ObjID" => SecsItem.A(GemValue.Ascii(carrier.Id)),
                "Capacity" => carrier.Capacity is null ? SecsItem.U1() : SecsItem.U1(carrier.Capacity.Value),
                "CarrierAccessingStatus" => SecsItem.U1(carrier.Accessing),
                "CarrierIDStatus" => SecsItem.U1(carrier.IdStatus),
                "ContentMap" => ContentMapItem(carrier),
                "LocationID" => SecsItem.A(GemValue.Ascii(carrier.LocationId)),
                "SlotMap" => SlotMapItem(carrier),
                "SlotMapStatus" => SecsItem.U1(carrier.SlotMapStatus),
                "SubstrateCount" => carrier.SubstrateCount is null ? SecsItem.U1() : SecsItem.U1(carrier.SubstrateCount.Value),
                "Usage" => SecsItem.A(GemValue.Ascii(carrier.Usage)),
                _ => SecsItem.L(),
            };
            return true;
        }
    }

    internal bool TryGetPortAttribute(string portId, string attribute, out SecsItem value)
    {
        lock (_gate)
        {
            value = SecsItem.L();
            var port = _ports.FirstOrDefault(item => item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == portId.Trim());
            if (port is null)
            {
                return false;
            }

            byte transfer = port.TransferState ?? E87Codes.TransferBlocked;
            value = attribute switch
            {
                "ObjType" => SecsItem.A("Port"),
                "ObjID" => SecsItem.U1(port.Id),
                "PortAccessMode" => SecsItem.U1(port.AccessMode),
                "PortAssociationState" => SecsItem.U1(port.AssociationState),
                "PortReservationState" => SecsItem.U1(port.ReservationState),
                "PortStateInfo" => SecsItem.L(SecsItem.U1(port.AssociationState), SecsItem.U1(transfer)),
                "PortTransferState" => SecsItem.U1(transfer),
                _ => SecsItem.L(),
            };
            return true;
        }
    }

    #endregion
}
