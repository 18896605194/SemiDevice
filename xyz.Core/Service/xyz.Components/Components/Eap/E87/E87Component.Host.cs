using xyz.Common.Log;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Secs;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// E87 的 Host 报文：S3F17 载具动作（ProceedWithCarrier、CancelCarrier、CancelCarrierAtPort、CarrierRelease、CarrierReCreate）、
/// S3F25 端口动作（启停用、改存取方式）、S3F27 改存取方式、S3F15 多块询问。都要 ON-LINE REMOTE；在链路的派发线程上跑。
/// 先查能不能做（回 CAACK），能做就给端口的状态机发消息（照老 CTC）。
/// </summary>
public partial class E87Component
{
    // 应答码、原因码、协议常量都在 E87Codes.cs（E87Caack、E87Reason、E87Codes）

    /// <summary>Host 在 S3F17 里能给的载具属性（照 _carrier 对象的属性号）；只用槽图和片号表，别的认得、不用。</summary>
    private static readonly string[] CarrierAttributeNames =
    [
        "ObjType", "ObjID", "Capacity", "CarrierAccessingStatus", "CarrierIDStatus", "ContentMap", "LocationID", "SlotMap",
        "SlotMapStatus", "SubstrateCount", "Usage",
    ];

    /// <summary>S3F15 多块询问 → S3F16 GRANT=0。</summary>
    private SecsReply Inquire(HsmsMessage message)
    {
        return SecsReply.Of(SecsItem.B(E87Codes.Granted));
    }

    #region S3F17 载具动作

    /// <summary>Host 给的载具属性里用得上的（没给的为 null）。</summary>
    private sealed class CarrierAttributes
    {
        public byte[]? SlotMap { get; set; }

        public List<(string LotId, string SubstrateId)>? ContentMap { get; set; }
    }

    /// <summary>
    /// S3F17 载具动作 → S3F18 L[2]{CAACK, 错误表}：L[5]{DATAID, CARRIERACTION, CARRIERSPEC, PTN, L{L[2]{ATTRID, ATTRDATA}}}。
    /// </summary>
    private SecsReply CarrierAction(HsmsMessage message)
    {
        #region 读报文：动作名、载具号、端口号、载具属性

        // 报文结构、类型不对 SecsRead 直接抛（链路回 S9F7）；属性写错的攒进 errors，回 CAACK=3
        var body = SecsRead.List(SecsRead.Body(message), "S3F17", 5);
        string action = SecsRead.Text(body[1], "CARRIERACTION").Trim();
        string carrierId = SecsRead.Text(body[2], "CARRIERSPEC").Trim();
        byte? ptn = body[3].Count == 0 ? null : SecsRead.Code(body[3], "PTN");
        var errors = new List<E5Error>();
        var attributes = ReadAttributes(SecsRead.List(body[4], "属性表"), errors);
        if (errors.Count > 0)
        {
            return Ack(E87Caack.InvalidData, errors);
        }

        #endregion

        #region EAP 自己的检查：要 ON-LINE REMOTE

        // 只跟"Host 发的"有关，手动页不管这个，所以留在 EAP 层；端口能不能动由端口自己的入口查
        if (_gem is null || !_gem.IsRemote)
        {
            return Ack(E87Caack.CannotPerformNow, [E5Error.NotRemote()]);
        }

        #endregion

        #region 按动作分派：在 E87 锁里查载具 / 端口状态机、改状态，攒下的设备动作（Load、Unload）出锁再做

        var reply = Locked(() =>
        {
            switch (action.ToUpperInvariant())
            {
                case "PROCEEDWITHCARRIER":
                    return ProceedWithCarrier(carrierId, ptn, attributes);

                case "CANCELCARRIER":
                    return CancelCarrier(carrierId, ptn);

                case "CANCELCARRIERATPORT":
                    return CancelCarrierAtPort(ptn);

                case "CARRIERRELEASE":
                    return CarrierRelease(carrierId, ptn);

                case "CARRIERRECREATE":
                    return CarrierReCreate(carrierId, ptn);

                default:
                    return Ack(E87Caack.InvalidCommand, [E5Error.Of(E5Error.UnsupportedOption, $"CARRIERACTION {action} not supported")]);
            }
        });

        #endregion

        LogHelper.Info(Name, $"Host 载具动作 {action} {carrierId} 端口 {ptn?.ToString() ?? "-"}");
        return reply;
    }

    /// <summary>
    /// ProceedWithCarrier 分两次（照 CTC）：ID 等 Host 的 → 认定（#8），Load；读码失败的端口（带 PTN）→ Host 给的号建对象并认定（#1、#4），Load。
    /// 槽图等 Host 的 → 带了槽图就跟读到的比，对不上拒掉；片号表写进晶圆账，槽图认定（#15，料到了）。
    /// </summary>
    private SecsReply ProceedWithCarrier(string carrierId, byte? ptn, CarrierAttributes attributes)
    {
        #region 载具号格式：E39 的 ObjID

        var idError = CheckCarrierId(carrierId);
        if (idError is not null)
        {
            return Ack(E87Caack.InvalidData, [idError]);
        }

        #endregion

        #region 没有这个载具对象：读码失败的口，Host 带端口号给号（PWC Type 4）→ 建对象直接认定，会 Load

        var port = FindCarrier(carrierId);
        if (port is null)
        {
            var target = PortById(ptn);
            if (target is null || target.HasCarrier || !target._carrier.IsArrived)
            {
                return Ack(E87Caack.InvalidData, [E5Error.Of(E5Error.UnknownObject, $"_carrier {carrierId} not found")]);
            }

            // 建对象直接认定：ID 状态机进 Verified，进入动作 EnterVerified 里 LoadLater，出了 E87 的锁才调端口的 Load()
            CreateCarrier(target, carrierId, E87CarrierIdMessage.HostProceed);
            return Ack(E87Caack.Ok, []);
        }

        #endregion

        #region 带了端口号要对得上

        if (ptn is not null && port.Id != ptn.Value)
        {
            return Ack(E87Caack.InvalidData, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"_carrier {carrierId} is not at port {ptn}")]);
        }

        #endregion

        #region 第一次：ID 在等 Host → 认定 ID，会 Load

        if (port.CarrierIdMachine.State == E87CarrierIdState.WaitingForHost)
        {
            if (attributes.SlotMap is not null || attributes.ContentMap is not null)
            {
                LogHelper.Info(Name, $"载具 {carrierId} 认定 ID 时带的槽图 / 片号表不用（照 CTC），等槽图读到后第二次 ProceedWithCarrier 再给");
            }

            // 认定 ID → 这里触发 Load：ID 状态机 WaitingForHost → Verified，进入动作 EnterVerified 里 LoadLater，
            // 出了 E87 的锁才调端口的 Load()（端口自己查 状态 → 资源 → 互锁）。Host 这时已经收到 CAACK=0，Load 被拒只记日志
            port.CarrierIdMachine.Post(E87CarrierIdMessage.HostProceed);
            return Ack(E87Caack.Ok, []);
        }

        #endregion

        #region 第二次：槽图在等 Host → 比对槽图、写片号表、认定槽图（料到了），不 Load

        if (port.SlotMapMachine.State == E87SlotMapState.WaitingForHost)
        {
            var error = CheckHostSlotMap(port, attributes);
            if (error is not null)
            {
                return Ack(E87Caack.InvalidData, [error]);
            }

            // 槽图状态机 WaitingForHost → Verified，进入动作里报 #15、载具记已认定、出锁后通知 E90 建片对象
            WriteContentMap(port, attributes.ContentMap);
            port.SlotMapMachine.Post(E87SlotMapMessage.HostProceed);
            return Ack(E87Caack.Ok, []);
        }

        #endregion

        // ID、槽图都不在等 Host：这时候没什么可认定的
        return Ack(E87Caack.InvalidState, [E5Error.Of(E5Error.InvalidState, $"_carrier {carrierId} is not waiting for host")]);
    }

    /// <summary>
    /// 第二次 PWC 带的槽图、片号表对不对（照 CTC）：槽图要跟设备读到的一样，片号表要一槽一项；没带的不查。返回 null 是对的。
    /// </summary>
    private static E5Error? CheckHostSlotMap(E87LoadPort port, CarrierAttributes attributes)
    {
        var read = port._carrier.SlotMap;
        var expected = attributes.SlotMap;
        if (expected is not null && !expected.SequenceEqual(read.Select(slot => (byte)slot)))
        {
            return E5Error.Of(E5Error.InvalidAttributeValue, "SlotMap does not match");
        }

        var content = attributes.ContentMap;
        if (content is not null && content.Count != read.Count)
        {
            return E5Error.Of(E5Error.InvalidAttributeValue, "ContentMap length differs from slot count");
        }

        return null;
    }

    /// <summary>Host 给的片号表写进晶圆账（片号、批次号），空槽、空项跳过（CTC 也是这样）。</summary>
    private static void WriteContentMap(E87LoadPort port, List<(string LotId, string SubstrateId)>? content)
    {
        var ledger = WaferManagerComponent.Current;
        if (ledger is null || content is null)
        {
            return;
        }

        string name = port._loadport.Name;
        for (int index = 0; index < content.Count; index++)
        {
            var (lotId, substrateId) = content[index];
            if (ledger.Get(name, index + 1) is null)
            {
                continue;
            }

            if (substrateId.Length > 0)
            {
                ledger.SetWaferId(name, index + 1, substrateId);
            }

            if (lotId.Length > 0)
            {
                ledger.SetLotId(name, index + 1, lotId);
            }
        }
    }

    /// <summary>CancelCarrier：按载具号找，取消（照 CTC）。</summary>
    private SecsReply CancelCarrier(string carrierId, byte? ptn)
    {
        var port = FindCarrier(carrierId);
        if (port is null)
        {
            return Ack(E87Caack.InvalidData, [E5Error.Of(E5Error.UnknownObject, $"_carrier {carrierId} not found")]);
        }

        if (ptn is not null && port.Id != ptn.Value)
        {
            return Ack(E87Caack.InvalidData, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"_carrier {carrierId} is not at port {ptn}")]);
        }

        return Cancel(port);
    }

    /// <summary>CancelCarrierAtPort：取消这个端口上的载具；没有载具对象（读码失败的）直接放行这一盒。</summary>
    private SecsReply CancelCarrierAtPort(byte? ptn)
    {
        var port = PortById(ptn);
        if (port is null)
        {
            return Ack(E87Caack.InvalidData, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"PTN {ptn?.ToString() ?? "missing"} invalid")]);
        }

        if (port.HasCarrier)
        {
            return Cancel(port);
        }

        if (!port._carrier.IsArrived || port.Released)
        {
            return Ack(E87Caack.InvalidState, [E5Error.Of(E5Error.InvalidState, $"No carrier to cancel at port {port.Id}")]);
        }

        port.Released = true;
        port.TransferMachine.Refresh();
        return Ack(E87Caack.Ok, []);
    }

    /// <summary>端口正在动作（Load、Unload 中）：这时候取消、放行，动作完了盒子还会被 Load 着卡住，先不收。返回 null 是可以。</summary>
    private static SecsReply? RejectIfMoving(E87LoadPort port)
    {
        var device = port._loadport;
        return device.IsIdle || device.IsLoaded
            ? null
            : Ack(E87Caack.CannotPerformNow, [E5Error.Of(E5Error.Busy, $"Port {port.Id} is busy")]);
    }

    /// <summary>
    /// 取消（照 CTC）：开始取放以后不能取消；ID、槽图哪个在等 Host 就记哪个核对不过（#9 / #16，原因 5），取消关联，
    /// Load 着的卸下来，这一盒放行（卸好、空闲了端口转等取）。
    /// </summary>
    private SecsReply Cancel(E87LoadPort port)
    {
        if (port.AccessMachine.State != E87AccessState.NotAccessed)
        {
            return Ack(E87Caack.InvalidState, [E5Error.Of(E5Error.InvalidState, $"_carrier {port.CarrierId} is already accessed")]);
        }

        if (port.Released)
        {
            return Ack(E87Caack.InvalidState, [E5Error.Of(E5Error.InvalidState, $"_carrier {port.CarrierId} is already cancelled")]);
        }

        var moving = RejectIfMoving(port);
        if (moving is not null)
        {
            return moving;
        }

        port.CarrierIdMachine.Post(E87CarrierIdMessage.Cancel);
        port.SlotMapMachine.Post(E87SlotMapMessage.Cancel);
        port.AssociationMachine.Post(E87AssociationMessage.Dissociate);
        port.Released = true;
        port.UnloadLater("载具被 Host 取消了");
        port.TransferMachine.Refresh();
        return Ack(E87Caack.Ok, []);
    }

    /// <summary>
    /// CarrierRelease：Host 叫把这一盒放出去（照 CTC）——在取放的不行；Load 着的卸下来，卸好、空闲了端口转等取。
    /// LoadPort 的 EC AutoUnload 关着时，干完的载具靠它卸。
    /// </summary>
    private SecsReply CarrierRelease(string carrierId, byte? ptn)
    {
        var port = FindCarrier(carrierId);
        if (port is null || (ptn is not null && port.Id != ptn.Value))
        {
            return Ack(E87Caack.InvalidData, [E5Error.Of(E5Error.UnknownObject, $"_carrier {carrierId} not found at port {ptn?.ToString() ?? "-"}")]);
        }

        if (port.AccessMachine.State == E87AccessState.InAccess)
        {
            return Ack(E87Caack.InvalidState, [E5Error.Of(E5Error.InvalidState, $"_carrier {carrierId} is in access")]);
        }

        var moving = RejectIfMoving(port);
        if (moving is not null)
        {
            return moving;
        }

        port.Released = true;
        port.UnloadLater("Host 放行载具");
        port.TransferMachine.Refresh();
        return Ack(E87Caack.Ok, []);
    }

    /// <summary>
    /// CarrierReCreate：等取的载具重来一遍（照 CTC）——删掉原来的对象（#21），重新读码，读到号重新等 Host 核对。
    /// 端口要在等取；取放过的载具不行（设备那边的取放进度回不去）。
    /// </summary>
    private SecsReply CarrierReCreate(string carrierId, byte? ptn)
    {
        var port = PortById(ptn) ?? FindCarrier(carrierId);
        if (port is null || !port._carrier.IsArrived)
        {
            return Ack(E87Caack.InvalidData, [E5Error.Of(E5Error.UnknownObject, $"_carrier {carrierId} not found")]);
        }

        if (port.TransferMachine.State != E87TransferState.ReadyToUnload)
        {
            return Ack(E87Caack.InvalidState, [E5Error.Of(E5Error.InvalidState, $"Port {port.Id} is not ready to unload")]);
        }

        if (port.HasCarrier && port.AccessMachine.State != E87AccessState.NotAccessed)
        {
            return Ack(E87Caack.InvalidState, [E5Error.Of(E5Error.InvalidState, $"_carrier {port.CarrierId} is already accessed")]);
        }

        DeleteCarrier(port);
        port.Released = false;
        var device = port._loadport;
        var carrier = port._carrier;
        Later(() =>
        {
            if (!carrier.ReadId())
            {
                LogHelper.Warn(Name, $"{device.Name} CarrierReCreate 重新读码没发起来，等 Host 带端口号给号");
            }
        });
        port.TransferMachine.Refresh();
        return Ack(E87Caack.Ok, []);
    }

    private E87LoadPort? PortById(byte? ptn)
    {
        return ptn is null ? null : _ports.FirstOrDefault(port => port.Id == ptn.Value);
    }

    /// <summary>
    /// 读 S3F17 带的属性：SlotMap（L{U1}）、ContentMap（L{L[2]{A, A}}）用得上；_carrier 对象别的属性认得、不用；
    /// ATTRID 可以是名字或 _carrier 对象的属性号；不认识的、格式不对的记错误。
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
            if (name is null)
            {
                errors.Add(E5Error.Of(E5Error.UnknownAttribute, $"_carrier attribute {SecsTextOf(pair[0])} not allowed"));
                continue;
            }

            var value = pair[1];
            try
            {
                if (name == "SlotMap")
                {
                    result.SlotMap = SecsRead.List(value, "SlotMap").Select(slot => SecsRead.Code(slot, "SlotStatus")).ToArray();
                }
                else if (name == "ContentMap")
                {
                    result.ContentMap = SecsRead.List(value, "ContentMap").Select(slot =>
                    {
                        var entry = SecsRead.List(slot, "ContentMap 一槽");
                        return entry.Count < 2
                            ? (string.Empty, string.Empty)
                            : (SecsRead.Text(entry[0], "LotID").Trim(), SecsRead.Text(entry[1], "SubstrateID").Trim());
                    }).ToList();
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

    /// <summary>载具号是 E39 的 ObjID：1~80 个可见 ASCII，不能有 ? * ~ &gt; :。</summary>
    private static E5Error? CheckCarrierId(string carrierId)
    {
        bool valid = carrierId.Length is >= 1 and <= E87Codes.MaxObjectIdLength
            && carrierId.All(ch => ch >= ' ' && ch <= '~' && !E87Codes.ForbiddenIdChars.Contains(ch));
        return valid ? null : E5Error.Of(E5Error.InvalidAttributeValue, $"CarrierID {carrierId} invalid");
    }

    private static SecsReply Ack(E87Caack caack, IReadOnlyList<E5Error> errors)
    {
        return SecsReply.Of(SecsItem.L(SecsItem.U1((byte)caack), E5Error.List(errors)));
    }

    #endregion

    #region S3F25 端口动作、S3F27 改存取方式

    /// <summary>
    /// S3F25 端口动作 → S3F26 L[2]{CAACK, 错误表}：L[3]{PORTACTION, PTN, L{L[2]{PARAMNAME, PARAMVAL}}}。
    /// InService / OutOfService（带不带空格都认）、ChangeServiceStatus（ServiceStatus 0 停用 / 1 启用）、ChangeAccess（AccessMode 0 手动 / 1 自动）；
    /// 别的（预约这些）回 CAACK=1。
    /// </summary>
    private SecsReply PortAction(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S3F25", 3);
        string action = SecsRead.Text(body[0], "PORTACTION").Replace(" ", string.Empty).Trim().ToUpperInvariant();
        byte ptn = SecsRead.Code(body[1], "PTN");
        var parameters = SecsRead.List(body[2], "参数表").Select(item =>
        {
            var pair = SecsRead.List(item, "L{PARAMNAME, PARAMVAL}", 2);
            return (Name: SecsRead.Text(pair[0], "PARAMNAME").Trim(), Value: pair[1]);
        }).ToList();

        if (_gem is null || !_gem.IsRemote)
        {
            return Ack(E87Caack.CannotPerformNow, [E5Error.NotRemote()]);
        }

        SecsItem? Parameter(string name)
        {
            return parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
        }

        var reply = Locked(() =>
        {
            var port = PortById(ptn);
            if (port is null)
            {
                return Ack(E87Caack.InvalidData, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"PTN {ptn} invalid")]);
            }

            switch (action)
            {
                case "INSERVICE":
                case "OUTOFSERVICE":
                    return ChangeService(port, action == "INSERVICE");

                case "CHANGESERVICESTATUS":
                    var status = Parameter("ServiceStatus");
                    if (status is null)
                    {
                        return Ack(E87Caack.InvalidData, [E5Error.Of(E5Error.InsufficientParameters, "ServiceStatus missing")]);
                    }

                    return ChangeService(port, SecsRead.Code(status, "ServiceStatus") != 0);

                case "CHANGEACCESS":
                    var mode = Parameter("AccessMode");
                    if (mode is null)
                    {
                        return Ack(E87Caack.InvalidData, [E5Error.Of(E5Error.InsufficientParameters, "AccessMode missing")]);
                    }

                    bool auto = SecsRead.Code(mode, "AccessMode") != 0;
                    var device = port._loadport;
                    return Ack(E87Caack.Ok, []).Then(() => device.SetAutoMode(auto));

                default:
                    return Ack(E87Caack.InvalidCommand, [E5Error.Of(E5Error.ParametersImproperlySpecified, $"PORTACTION {action} unknown")]);
            }
        });
        LogHelper.Info(Name, $"Host 端口动作 {action} 端口 {ptn}");
        return reply;
    }

    /// <summary>Host 启用 / 停用端口：搬运状态机跟着转（#2、#4 / #3）；停用的端口 E84 不交接。</summary>
    private static SecsReply ChangeService(E87LoadPort port, bool inService)
    {
        port.TransferMachine.InService = inService;
        port.TransferMachine.Refresh();
        return Ack(E87Caack.Ok, []);
    }

    /// <summary>
    /// S3F27 改存取方式 → S3F28 L[2]{CAACK, L{L[3]{PTN, ERRCODE, ERRTEXT}}}：L[2]{ACCESSMODE（0 手动 / 1 自动）, L{PTN}}，端口表空 = 全部端口。
    /// 已经是这个方式的照收不报事件。
    /// </summary>
    private SecsReply ChangeAccess(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S3F27", 2);
        bool auto = SecsRead.Code(body[0], "ACCESSMODE") != 0;
        var requested = SecsRead.List(body[1], "PTN 表").Select(item => SecsRead.Code(item, "PTN")).ToList();
        if (_gem is null || !_gem.IsRemote)
        {
            var notRemote = E5Error.NotRemote();
            return SecsReply.Of(SecsItem.L(SecsItem.U1((byte)E87Caack.CannotPerformNow),
                SecsItem.L(SecsItem.L(SecsItem.U1(0), SecsItem.U2(notRemote.Code), SecsItem.A(notRemote.Text)))));
        }

        var failures = new List<SecsItem>();
        var targets = new List<ILoadPort>();
        lock (_gate)
        {
            var ids = requested.Count == 0 ? _ports.Select(port => port.Id).ToList() : requested;
            foreach (byte ptn in ids)
            {
                var port = PortById(ptn);
                if (port is null)
                {
                    failures.Add(SecsItem.L(SecsItem.U1(ptn), SecsItem.U2(E5Error.ParametersImproperlySpecified), SecsItem.A("Invalid PTN")));
                }
                else
                {
                    targets.Add(port._loadport);
                }
            }
        }

        var caack = failures.Count == 0 ? E87Caack.Ok : E87Caack.InvalidData;
        return SecsReply.Of(SecsItem.L(SecsItem.U1((byte)caack), SecsItem.L(failures))).Then(() =>
        {
            foreach (var device in targets)
            {
                device.SetAutoMode(auto);
            }
        });
    }

    #endregion
}
