using xyz.Common.Log;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Secs;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// E87 的 Host 报文：S3F17 载具动作（ProceedWithCarrier、CancelCarrier、Bind……）、S3F25 端口动作（预约、启停用、改存取方式）、
/// S3F27 改存取方式、S3F15 多块询问。都要 ON-LINE REMOTE；在链路的派发线程上跑。
/// </summary>
public partial class E87Component
{
    /// <summary>CAACK：0 收下。</summary>
    private const byte CaackOk = 0;

    /// <summary>CAACK 1：没有这个动作（或不支持）。</summary>
    private const byte CaackInvalidCommand = 1;

    /// <summary>CAACK 2：现在做不了（不是 ON-LINE REMOTE）。</summary>
    private const byte CaackCannotPerformNow = 2;

    /// <summary>CAACK 3：数据、参数不对。</summary>
    private const byte CaackInvalidData = 3;

    /// <summary>CAACK 5：当前状态下不能做。</summary>
    private const byte CaackInvalidState = 5;

    /// <summary>GRANT：可以发。</summary>
    private const byte Granted = 0;

    /// <summary>E39 的 ObjID 最长 80 个字符，不能有这几个字符。</summary>
    private const int MaxObjectIdLength = 80;

    private const string ForbiddenIdChars = "?*~>:";

    /// <summary>Host 在 S3F17 里能给的载具属性（照 Carrier 对象的属性号）。</summary>
    private static readonly string[] CarrierAttributeNames =
    [
        "ObjType", "ObjID", "Capacity", "CarrierAccessingStatus", "CarrierIDStatus", "ContentMap", "LocationID", "SlotMap",
        "SlotMapStatus", "SubstrateCount", "Usage",
    ];

    /// <summary>E87 定义了、本机不支持的载具动作（内部缓冲设备用的、读写标签的）。</summary>
    private static readonly string[] UnsupportedActions =
    [
        "CarrierReCreate", "CarrierRelease", "CancelCarrierOut", "CarrierIn", "CarrierOut", "CancelAllCarrierOut",
    ];

    /// <summary>S3F15 多块询问 → S3F16 GRANT=0。</summary>
    private SecsReply Inquire(HsmsMessage message)
    {
        return SecsReply.Of(SecsItem.B(Granted));
    }

    #region S3F17 载具动作

    /// <summary>Host 给的载具属性（没给的为 null）。</summary>
    private sealed class CarrierAttributes
    {
        public byte? Capacity { get; set; }

        public byte? SubstrateCount { get; set; }

        public byte[]? SlotMap { get; set; }

        public List<(string LotId, string SubstrateId)>? ContentMap { get; set; }

        public string? Usage { get; set; }
    }

    /// <summary>
    /// S3F17 载具动作 → S3F18 L[2]{CAACK, 错误表}：L[5]{DATAID, CARRIERACTION, CARRIERSPEC, PTN, L{L[2]{ATTRID, ATTRDATA}}}。
    /// </summary>
    private SecsReply CarrierAction(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S3F17", 5);
        string action = SecsRead.Text(body[1], "CARRIERACTION").Trim();
        string carrierId = SecsRead.Text(body[2], "CARRIERSPEC").Trim();
        byte? ptn = body[3].Count == 0 ? null : SecsRead.Code(body[3], "PTN");
        var errors = new List<E5Error>();
        var attributes = ReadAttributes(SecsRead.List(body[4], "属性表"), errors);
        if (errors.Count > 0)
        {
            return Ack(CaackInvalidData, errors);
        }

        if (_gem is null || !_gem.IsRemote)
        {
            return Ack(CaackCannotPerformNow, [E5Error.NotRemote()]);
        }

        if (UnsupportedActions.Any(name => string.Equals(name, action, StringComparison.OrdinalIgnoreCase)))
        {
            return Ack(CaackInvalidCommand, [E5Error.Of(E5Error.UnsupportedOption, $"{action} not supported")]);
        }

        var actions = new List<Action>();
        SecsReply reply;
        lock (_gate)
        {
            reply = action.ToUpperInvariant() switch
            {
                "PROCEEDWITHCARRIER" => ProceedWithCarrier(carrierId, ptn, attributes, actions),
                "CANCELCARRIER" => CancelCarrier(carrierId, ptn, actions),
                "CANCELCARRIERATPORT" => CancelCarrierAtPort(ptn, actions),
                "BIND" => Bind(carrierId, ptn, attributes),
                "CANCELBIND" => CancelBind(carrierId, ptn),
                "CARRIERNOTIFICATION" => CarrierNotification(carrierId, attributes),
                "CANCELCARRIERNOTIFICATION" => CancelCarrierNotification(carrierId),
                _ => Ack(CaackInvalidCommand, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"CARRIERACTION {action} unknown")]),
            };
        }

        Run(actions);
        LogHelper.Info(Name, $"Host 载具动作 {action} {carrierId} 端口 {ptn?.ToString() ?? "-"}");
        return reply;
    }

    /// <summary>
    /// ProceedWithCarrier：ID 等 Host 的 → 认定（#8），Load；槽图等 Host 的 → 认定（#15），料到了；
    /// 没预告、读码失败的端口（带 PTN）→ Host 给的号建对象并认定（#4），Load。
    /// </summary>
    private SecsReply ProceedWithCarrier(string carrierId, byte? ptn, CarrierAttributes attributes, List<Action> actions)
    {
        var idError = CheckCarrierId(carrierId);
        if (idError is not null)
        {
            return Ack(CaackInvalidData, [idError]);
        }

        if (!_carriers.TryGetValue(carrierId, out var carrier))
        {
            var port = ptn is null ? null : _ports.FirstOrDefault(item => item.Id == ptn.Value);
            if (port is null || !port.ReadFailed || !port.Present || port.Carrier is not null)
            {
                return Ack(CaackInvalidData, [E5Error.Of(E5Error.UnknownObject, $"Carrier {carrierId} not found")]);
            }

            // 读码失败、Host 给号（PWC Type 4）：建对象（#1、#4、#12、#17）、关联、认定
            carrier = new E87Carrier { Id = carrierId, Port = port, Arrived = true };
            var applied = Apply(carrier, attributes, slotMapAllowed: true);
            if (applied is not null)
            {
                return Ack(CaackInvalidData, [applied]);
            }

            _carriers[carrierId] = carrier;
            port.Carrier = carrier;
            port.ReadFailed = false;
            ReportCarrier(CarrierTrans01, carrier, port);
            ReportCarrier(CarrierTrans12, carrier, port);
            ReportCarrier(CarrierTrans17, carrier, port);
            ReportAssociation(port);
            Verify(port, carrier, CarrierTrans04, actions);
            return Ack(CaackOk, []);
        }

        var at = carrier.Port;
        if (ptn is not null && (at is null || at.Id != ptn.Value))
        {
            return Ack(CaackInvalidData, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"Carrier {carrierId} is not at port {ptn}")]);
        }

        if (at is not null && carrier.IdStatus == E87Codes.IdWaitingForHost)
        {
            var applied = Apply(carrier, attributes, slotMapAllowed: carrier.ExpectedSlotMap is null);
            if (applied is not null)
            {
                return Ack(CaackInvalidData, [applied]);
            }

            Verify(at, carrier, CarrierTrans08, actions);
            return Ack(CaackOk, []);
        }

        if (at is not null && carrier.SlotMapStatus == E87Codes.MapWaitingForHost)
        {
            if (attributes.Capacity is not null || attributes.SlotMap is not null || attributes.SubstrateCount is not null)
            {
                return Ack(CaackInvalidData, [E5Error.Of(E5Error.ParametersImproperlySpecified, "Only ContentMap and Usage allowed now")]);
            }

            Apply(carrier, attributes, slotMapAllowed: false);
            MaterialVerified(at, carrier, CarrierTrans15, actions);
            return Ack(CaackOk, []);
        }

        return Ack(CaackInvalidState, [E5Error.Of(E5Error.InvalidState, $"Carrier {carrierId} is not waiting for host")]);
    }

    /// <summary>
    /// CancelCarrier：取放开始以后不能取消；只是预告的直接删（#21）；在端口上的按等 Host 的那一步记核对不过（#9 / #16，原因 5），卸下来等取走。
    /// </summary>
    private SecsReply CancelCarrier(string carrierId, byte? ptn, List<Action> actions)
    {
        if (!_carriers.TryGetValue(carrierId, out var carrier))
        {
            return Ack(CaackInvalidData, [E5Error.Of(E5Error.UnknownObject, $"Carrier {carrierId} not found")]);
        }

        if (ptn is not null && (carrier.Port is null || carrier.Port.Id != ptn.Value))
        {
            return Ack(CaackInvalidData, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"Carrier {carrierId} is not at port {ptn}")]);
        }

        return Cancel(carrier, actions);
    }

    private SecsReply Cancel(E87Carrier carrier, List<Action> actions)
    {
        if (carrier.Accessing != E87Codes.NotAccessed)
        {
            return Ack(CaackInvalidState, [E5Error.Of(E5Error.InvalidState, $"Carrier {carrier.Id} is already accessed")]);
        }

        if (carrier.IdStatus == E87Codes.IdVerifyFailed || carrier.SlotMapStatus == E87Codes.MapVerifyFailed)
        {
            return Ack(CaackInvalidState, [E5Error.Of(E5Error.InvalidState, $"Carrier {carrier.Id} is already cancelled")]);
        }

        var port = carrier.Port;
        if (port is null || !carrier.Arrived)
        {
            Remove(carrier);
            return Ack(CaackOk, []);
        }

        var device = port.Device;
        if (carrier.IdStatus != E87Codes.IdVerified)
        {
            carrier.IdStatus = E87Codes.IdVerifyFailed;
            ReportCarrier(CarrierTrans09, carrier, port, E87Codes.ReasonHostCancel);
            actions.Add(() => device.UpdateCarrierStatus(CarrierIdStatus.VerifyFailed, null));
        }
        else if (carrier.SlotMapStatus != E87Codes.MapVerified)
        {
            carrier.SlotMapStatus = E87Codes.MapVerifyFailed;
            ReportCarrier(CarrierTrans16, carrier, port, E87Codes.ReasonHostCancel);
            actions.Add(() => device.UpdateCarrierStatus(null, CarrierSlotMapStatus.VerifyFailed));
        }

        Reject(port, actions);
        return Ack(CaackOk, []);
    }

    /// <summary>CancelCarrierAtPort：取消这个端口上的载具；没有载具对象（读码失败、等 Host 给号的）直接不要这一盒。</summary>
    private SecsReply CancelCarrierAtPort(byte? ptn, List<Action> actions)
    {
        var port = ptn is null ? null : _ports.FirstOrDefault(item => item.Id == ptn.Value);
        if (port is null)
        {
            return Ack(CaackInvalidData, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"PTN {ptn?.ToString() ?? "missing"} invalid")]);
        }

        var carrier = port.Carrier;
        if (carrier is not null)
        {
            return Cancel(carrier, actions);
        }

        if (!port.Present)
        {
            return Ack(CaackInvalidState, [E5Error.Of(E5Error.InvalidState, $"No carrier at port {port.Id}")]);
        }

        port.ReadFailed = false;
        Reject(port, actions);
        return Ack(CaackOk, []);
    }

    /// <summary>
    /// Bind：Host 预告这个载具会到这个端口——端口要空着、没关联、没预约；建对象（#1、#2、#12、#17），端口关联、预约。
    /// </summary>
    private SecsReply Bind(string carrierId, byte? ptn, CarrierAttributes attributes)
    {
        var idError = CheckCarrierId(carrierId);
        if (idError is not null)
        {
            return Ack(CaackInvalidData, [idError]);
        }

        var port = ptn is null ? null : _ports.FirstOrDefault(item => item.Id == ptn.Value);
        if (port is null)
        {
            return Ack(CaackInvalidData, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"PTN {ptn?.ToString() ?? "missing"} invalid")]);
        }

        if (_carriers.ContainsKey(carrierId))
        {
            return Ack(CaackInvalidData, [E5Error.Of(E5Error.IdentifierInUse, $"Carrier {carrierId} already exists")]);
        }

        if (port.Present || port.Carrier is not null || port.Reserved || !port.InService)
        {
            return Ack(CaackInvalidState, [E5Error.Of(E5Error.InvalidState, $"Port {port.Id} is not free")]);
        }

        var carrier = new E87Carrier { Id = carrierId, Port = port, Bound = true };
        var applied = Apply(carrier, attributes, slotMapAllowed: true);
        if (applied is not null)
        {
            return Ack(CaackInvalidData, [applied]);
        }

        _carriers[carrierId] = carrier;
        port.Carrier = carrier;
        port.Reserved = true;
        ReportCarrier(CarrierTrans01, carrier, port);
        ReportCarrier(CarrierTrans02, carrier, port);
        ReportCarrier(CarrierTrans12, carrier, port);
        ReportCarrier(CarrierTrans17, carrier, port);
        ReportAssociation(port);
        ReportPort(ReservationGo, port);
        return Ack(CaackOk, []);
    }

    /// <summary>CancelBind：撤掉还没到的 Bind（按载具号或端口号找），删对象（#21），端口取消关联、预约。</summary>
    private SecsReply CancelBind(string carrierId, byte? ptn)
    {
        E87Carrier? carrier = null;
        if (carrierId.Length > 0)
        {
            _carriers.TryGetValue(carrierId, out carrier);
        }
        else if (ptn is not null)
        {
            carrier = _ports.FirstOrDefault(item => item.Id == ptn.Value)?.Carrier;
        }

        if (carrier is null || !carrier.Bound || (ptn is not null && carrier.Port?.Id != ptn.Value))
        {
            return Ack(CaackInvalidData, [E5Error.Of(E5Error.UnknownObject, "Bound carrier not found")]);
        }

        if (carrier.Arrived)
        {
            return Ack(CaackInvalidState, [E5Error.Of(E5Error.InvalidState, $"Carrier {carrier.Id} already arrived")]);
        }

        Remove(carrier);
        return Ack(CaackOk, []);
    }

    /// <summary>CarrierNotification：Host 预告这个载具会来（不定端口），建对象（#1、#2、#12、#17）；读码对上时关联端口。</summary>
    private SecsReply CarrierNotification(string carrierId, CarrierAttributes attributes)
    {
        var idError = CheckCarrierId(carrierId);
        if (idError is not null)
        {
            return Ack(CaackInvalidData, [idError]);
        }

        if (_carriers.ContainsKey(carrierId))
        {
            return Ack(CaackInvalidData, [E5Error.Of(E5Error.IdentifierInUse, $"Carrier {carrierId} already exists")]);
        }

        var carrier = new E87Carrier { Id = carrierId };
        var applied = Apply(carrier, attributes, slotMapAllowed: true);
        if (applied is not null)
        {
            return Ack(CaackInvalidData, [applied]);
        }

        _carriers[carrierId] = carrier;
        ReportCarrier(CarrierTrans01, carrier, null);
        ReportCarrier(CarrierTrans02, carrier, null);
        ReportCarrier(CarrierTrans12, carrier, null);
        ReportCarrier(CarrierTrans17, carrier, null);
        return Ack(CaackOk, []);
    }

    /// <summary>CancelCarrierNotification：撤掉还没到端口的预告（#21）。</summary>
    private SecsReply CancelCarrierNotification(string carrierId)
    {
        if (!_carriers.TryGetValue(carrierId, out var carrier) || carrier.Port is not null)
        {
            return Ack(CaackInvalidData, [E5Error.Of(E5Error.UnknownObject, $"Notified carrier {carrierId} not found")]);
        }

        Remove(carrier);
        return Ack(CaackOk, []);
    }

    /// <summary>
    /// 读 S3F17 带的属性：Capacity、SubstrateCount（U1）、SlotMap（L{U1}）、ContentMap（L{L[2]{A, A}}）、Usage（A），
    /// ATTRID 可以是名字或 Carrier 对象的属性号；别的属性、格式不对的记错误。
    /// </summary>
    private static CarrierAttributes ReadAttributes(IReadOnlyList<SecsItem> items, List<E5Error> errors)
    {
        var result = new CarrierAttributes();
        foreach (var item in items)
        {
            var pair = SecsRead.List(item, "L{ATTRID, ATTRDATA}", 2);
            string? name = pair[0].Format is SecsFormat.Ascii or SecsFormat.Jis8
                ? CarrierAttributeNames.FirstOrDefault(candidate => string.Equals(candidate, pair[0].GetString().Trim(), StringComparison.OrdinalIgnoreCase))
                : AttributeByNumber(SecsRead.Id(pair[0], "ATTRID"));
            var value = pair[1];
            try
            {
                switch (name)
                {
                    case "Capacity":
                        result.Capacity = SecsRead.Code(value, "Capacity");
                        break;

                    case "SubstrateCount":
                        result.SubstrateCount = SecsRead.Code(value, "SubstrateCount");
                        break;

                    case "SlotMap":
                        result.SlotMap = SecsRead.List(value, "SlotMap").Select(slot => SecsRead.Code(slot, "SlotStatus")).ToArray();
                        break;

                    case "ContentMap":
                        result.ContentMap = SecsRead.List(value, "ContentMap").Select(slot =>
                        {
                            var entry = SecsRead.List(slot, "ContentMap 一槽");
                            return entry.Count < 2
                                ? (string.Empty, string.Empty)
                                : (SecsRead.Text(entry[0], "LotID").Trim(), SecsRead.Text(entry[1], "SubstrateID").Trim());
                        }).ToList();
                        break;

                    case "Usage":
                        result.Usage = SecsRead.Text(value, "Usage").Trim();
                        break;

                    default:
                        errors.Add(E5Error.Of(E5Error.UnknownAttribute, $"Carrier attribute {SecsTextOf(pair[0])} not allowed"));
                        break;
                }
            }
            catch (SecsException exception)
            {
                errors.Add(E5Error.Of(E5Error.InvalidAttributeValue, $"{name} invalid: {exception.Message}"));
            }
        }

        return result;
    }

    private static string? AttributeByNumber(uint number)
    {
        return number >= 1 && number <= CarrierAttributeNames.Length ? CarrierAttributeNames[number - 1] : null;
    }

    private static string SecsTextOf(SecsItem item)
    {
        return item.Format is SecsFormat.Ascii or SecsFormat.Jis8 ? item.GetString() : item.ToString();
    }

    /// <summary>
    /// 把 Host 给的属性记到载具上：槽图只能给一次（给过了再给算错）；槽数跟槽图、片号表长度对不上算错。返回 null 是记好了。
    /// </summary>
    private static E5Error? Apply(E87Carrier carrier, CarrierAttributes attributes, bool slotMapAllowed)
    {
        if (attributes.SlotMap is not null && !slotMapAllowed)
        {
            return E5Error.Of(E5Error.InvalidAttributeValue, "SlotMap already given");
        }

        byte? capacity = attributes.Capacity ?? carrier.Capacity;
        if (capacity is not null && attributes.SlotMap is not null && attributes.SlotMap.Length != capacity.Value)
        {
            return E5Error.Of(E5Error.InvalidAttributeValue, "SlotMap length differs from Capacity");
        }

        if (capacity is not null && attributes.ContentMap is not null && attributes.ContentMap.Count != capacity.Value)
        {
            return E5Error.Of(E5Error.InvalidAttributeValue, "ContentMap length differs from Capacity");
        }

        carrier.Capacity = capacity;
        carrier.SubstrateCount = attributes.SubstrateCount ?? carrier.SubstrateCount;
        carrier.ExpectedSlotMap = attributes.SlotMap ?? carrier.ExpectedSlotMap;
        carrier.ContentMap = attributes.ContentMap ?? carrier.ContentMap;
        carrier.Usage = attributes.Usage ?? carrier.Usage;
        return null;
    }

    /// <summary>载具号是 E39 的 ObjID：1~80 个可见 ASCII，不能有 ? * ~ &gt; :。</summary>
    private static E5Error? CheckCarrierId(string carrierId)
    {
        bool valid = carrierId.Length is >= 1 and <= MaxObjectIdLength
            && carrierId.All(ch => ch >= ' ' && ch <= '~' && !ForbiddenIdChars.Contains(ch));
        return valid ? null : E5Error.Of(E5Error.InvalidAttributeValue, $"CarrierID {carrierId} invalid");
    }

    private static SecsReply Ack(byte caack, IReadOnlyList<E5Error> errors)
    {
        return SecsReply.Of(SecsItem.L(SecsItem.U1(caack), E5Error.List(errors)));
    }

    #endregion

    #region S3F25 端口动作、S3F27 改存取方式

    /// <summary>
    /// S3F25 端口动作 → S3F26 L[2]{CAACK, 错误表}：L[3]{PORTACTION, PTN, L{L[2]{PARAMNAME, PARAMVAL}}}。
    /// ReserveAtPort / CancelReservationAtPort、InService / OutOfService（带不带空格都认）、ChangeServiceStatus（ServiceStatus 0/1）、
    /// ChangeAccess（AccessMode 0 手动 / 1 自动）。
    /// </summary>
    private SecsReply PortAction(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S3F25", 3);
        string action = SecsRead.Text(body[0], "PORTACTION").Replace(" ", string.Empty).Trim();
        byte ptn = SecsRead.Code(body[1], "PTN");
        var parameters = SecsRead.List(body[2], "参数表").Select(item =>
        {
            var pair = SecsRead.List(item, "L{PARAMNAME, PARAMVAL}", 2);
            return (Name: SecsRead.Text(pair[0], "PARAMNAME").Trim(), Value: pair[1]);
        }).ToList();

        if (_gem is null || !_gem.IsRemote)
        {
            return Ack(CaackCannotPerformNow, [E5Error.NotRemote()]);
        }

        bool? auto = null;
        SecsReply reply;
        lock (_gate)
        {
            var port = _ports.FirstOrDefault(item => item.Id == ptn);
            if (port is null)
            {
                return Ack(CaackInvalidData, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"PTN {ptn} invalid")]);
            }

            string upper = action.ToUpperInvariant();
            if (upper == "CHANGESERVICESTATUS")
            {
                var status = parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, "ServiceStatus", StringComparison.OrdinalIgnoreCase));
                if (status.Value is null)
                {
                    return Ack(CaackInvalidData, [E5Error.Of(E5Error.InsufficientParameters, "ServiceStatus missing")]);
                }

                upper = SecsRead.Code(status.Value, "ServiceStatus") == 0 ? "OUTOFSERVICE" : "INSERVICE";
            }

            switch (upper)
            {
                case "RESERVEATPORT":
                    if (port.Reserved || port.Present || port.Carrier is not null || !port.InService)
                    {
                        return Ack(CaackInvalidState, [E5Error.Of(E5Error.InvalidState, $"Port {port.Id} cannot be reserved")]);
                    }

                    port.Reserved = true;
                    ReportPort(ReservationGo, port);
                    reply = Ack(CaackOk, []);
                    break;

                case "CANCELRESERVATIONATPORT":
                    if (!port.Reserved)
                    {
                        return Ack(CaackInvalidState, [E5Error.Of(E5Error.InvalidState, $"Port {port.Id} is not reserved")]);
                    }

                    port.Reserved = false;
                    ReportPort(ReservationGoNot, port);
                    reply = Ack(CaackOk, []);
                    break;

                case "INSERVICE":
                case "OUTOFSERVICE":
                    port.InService = upper == "INSERVICE";
                    RefreshTransferState(port);
                    reply = Ack(CaackOk, []);
                    break;

                case "CHANGEACCESS":
                    var mode = parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, "AccessMode", StringComparison.OrdinalIgnoreCase));
                    if (mode.Value is null)
                    {
                        return Ack(CaackInvalidData, [E5Error.Of(E5Error.InsufficientParameters, "AccessMode missing")]);
                    }

                    if (port.Reserved)
                    {
                        return Ack(CaackInvalidState, [E5Error.Of(E5Error.InvalidState, $"Port {port.Id} is reserved")]);
                    }

                    auto = SecsRead.Code(mode.Value, "AccessMode") != 0;
                    reply = Ack(CaackOk, []);
                    break;

                default:
                    return Ack(CaackInvalidCommand, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"PORTACTION {action} unknown")]);
            }

            if (auto is not null)
            {
                bool value = auto.Value;
                var device = port.Device;
                reply = reply.Then(() => device.SetAutoMode(value));
            }
        }

        LogHelper.Info(Name, $"Host 端口动作 {action} 端口 {ptn}");
        return reply;
    }

    /// <summary>
    /// S3F27 改存取方式 → S3F28 L[2]{CAACK, L{L[3]{PTN, ERRCODE, ERRTEXT}}}：L[2]{ACCESSMODE（0 手动 / 1 自动）, L{PTN}}，端口表空 = 全部端口。
    /// 预约着的端口不能改（E87：预约期间存取方式冻结）；已经是这个方式的照收不报事件。
    /// </summary>
    private SecsReply ChangeAccess(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S3F27", 2);
        bool auto = SecsRead.Code(body[0], "ACCESSMODE") != 0;
        var requested = SecsRead.List(body[1], "PTN 表").Select(item => SecsRead.Code(item, "PTN")).ToList();
        if (_gem is null || !_gem.IsRemote)
        {
            var notRemote = E5Error.NotRemote();
            return SecsReply.Of(SecsItem.L(SecsItem.U1(CaackCannotPerformNow),
                SecsItem.L(SecsItem.L(SecsItem.U1(0), SecsItem.U2(notRemote.Code), SecsItem.A(notRemote.Text)))));
        }

        var failures = new List<SecsItem>();
        var targets = new List<ILoadPort>();
        lock (_gate)
        {
            var ids = requested.Count == 0 ? _ports.Select(port => port.Id).ToList() : requested;
            foreach (byte ptn in ids)
            {
                var port = _ports.FirstOrDefault(item => item.Id == ptn);
                if (port is null)
                {
                    failures.Add(SecsItem.L(SecsItem.U1(ptn), SecsItem.U2(E5Error.ParametersImproperlySpecified), SecsItem.A("Invalid PTN")));
                }
                else if (port.Reserved)
                {
                    failures.Add(SecsItem.L(SecsItem.U1(ptn), SecsItem.U2(E5Error.InvalidState), SecsItem.A("Port is reserved")));
                }
                else
                {
                    targets.Add(port.Device);
                }
            }
        }

        byte caack = failures.Count == 0 ? CaackOk : CaackInvalidState;
        return SecsReply.Of(SecsItem.L(SecsItem.U1(caack), SecsItem.L(failures))).Then(() =>
        {
            foreach (var device in targets)
            {
                device.SetAutoMode(auto);
            }
        });
    }

    #endregion
}
