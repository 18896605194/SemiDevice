using xyz.Elements.Attributes;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Drivers.Loadport;
using xyz.Secs.Gem;
using xyz.Secs.Gem.E87;
using xyz.Tools;

namespace xyz.Elements.Elements;

/// <summary>
/// 载具管理（SEMI E87，machine.xml 的 Eap 下的 E87 节点）：协议（6 个状态机、S3 报文、搬运状态计算）在 xyz.Secs 的 E87Engine，
/// 这里把每个 LoadPort 包成它要的端口（E87PortDevice），LoadPort 的回调（IE87Callback）交给它，E84 每拍问的搬运状态也问它
/// </summary>
[Element(description: "载具管理（SEMI E87）：载具 ID / 槽图跟 Host 核对、端口搬运状态，S3 报文")]
public class E87Element : ElementBase, IE87Callback, IE84Provider, IE87Element, IE87Settings, IGemEvents
{
    #region DV（事件带的数据）

    [DataVariable(ValueFormat.String, "载具号")]
    public readonly string CarrierIdData = E87Codes.CarrierId;

    [DataVariable(ValueFormat.Int, "端口号（PortID，按 LoadPort 先后从 1 开始）")]
    public readonly string PortIdData = E87Codes.PortId;

    [DataVariable(ValueFormat.Int, "载具 ID 状态：1 等 Host、2 认定、3 核对不过")]
    public readonly string CarrierIdStatusData = E87Codes.CarrierIdStatus;

    [DataVariable(ValueFormat.Int, "槽图状态：0 没读、1 等 Host、2 认定、3 核对不过")]
    public readonly string SlotMapStatusData = E87Codes.SlotMapStatus;

    [DataVariable(ValueFormat.Int, "载具取放状态：0 没取放、1 在取放、2 干完、3 中断")]
    public readonly string AccessingStatusData = E87Codes.AccessingStatus;

    [DataVariable(ValueFormat.Int, "载具槽数")]
    public readonly string CapacityData = E87Codes.Capacity;

    [DataVariable(ValueFormat.Int, "载具里的片数")]
    public readonly string SubstrateCountData = E87Codes.SubstrateCount;

    [DataVariable(ValueFormat.String, "槽图：L[槽数]{U1}，1 空、2 有片说不准、3 正常、4 叠片、5 交叉")]
    public readonly string SlotMapData = E87Codes.SlotMap;

    [DataVariable(ValueFormat.String, "片号表：L[槽数]{L[2]{批次号, 片号}}")]
    public readonly string ContentMapData = E87Codes.ContentMap;

    [DataVariable(ValueFormat.String, "载具在哪（端口名）")]
    public readonly string LocationIdData = E87Codes.LocationId;

    [DataVariable(ValueFormat.Int, "端口搬运状态：0 停用、1 挡着、2 等送、3 等取")]
    public readonly string TransferStateData = E87Codes.TransferState;

    [DataVariable(ValueFormat.Int, "端口存取方式：0 手动、1 自动")]
    public readonly string AccessModeData = E87Codes.AccessMode;

    [DataVariable(ValueFormat.Int, "端口关联：0 没关联、1 关联了载具")]
    public readonly string AssociationStateData = E87Codes.AssociationState;

    [DataVariable(ValueFormat.Int, "原因：0 等 Host 核对槽图、5 Host 取消")]
    public readonly string ReasonData = E87Codes.Reason;

    #endregion

    #region 事件：载具（E87 Carrier 状态机，由载具 ID、槽图、取放三个状态机报）

    [EventAttribut("载具对象建了（#1）", Data = new[] { E87Codes.CarrierId, E87Codes.LocationId })]
    public readonly string CarrierTrans01 = "CarrierSMTrans01";

    [EventAttribut("载具 ID 等 Host 核对（#3）", Data = new[] { E87Codes.CarrierId, E87Codes.CarrierIdStatus, E87Codes.LocationId, E87Codes.PortId })]
    public readonly string CarrierTrans03 = "CarrierSMTrans03";

    [EventAttribut("载具 ID 认定（#4，读码失败、Host 给号）", Data = new[] { E87Codes.CarrierId, E87Codes.CarrierIdStatus, E87Codes.LocationId, E87Codes.PortId })]
    public readonly string CarrierTrans04 = "CarrierSMTrans04";

    [EventAttribut("载具 ID 认定（#8，Host 让继续）", Data = new[] { E87Codes.CarrierId, E87Codes.CarrierIdStatus, E87Codes.LocationId, E87Codes.PortId })]
    public readonly string CarrierTrans08 = "CarrierSMTrans08";

    [EventAttribut("载具 ID 核对不过（#9，Host 取消）", Data = new[] { E87Codes.CarrierId, E87Codes.CarrierIdStatus, E87Codes.LocationId, E87Codes.PortId, E87Codes.Reason })]
    public readonly string CarrierTrans09 = "CarrierSMTrans09";

    [EventAttribut("载具槽图没读（#12）", Data = new[] { E87Codes.CarrierId, E87Codes.SlotMapStatus })]
    public readonly string CarrierTrans12 = "CarrierSMTrans12";

    [EventAttribut("载具槽图等 Host 核对（#14）", Data = new[] { E87Codes.CarrierId, E87Codes.SlotMapStatus, E87Codes.SlotMap, E87Codes.Capacity, E87Codes.SubstrateCount, E87Codes.LocationId, E87Codes.PortId, E87Codes.Reason })]
    public readonly string CarrierTrans14 = "CarrierSMTrans14";

    [EventAttribut("载具槽图认定（#15，Host 让继续）", Data = new[] { E87Codes.CarrierId, E87Codes.SlotMapStatus, E87Codes.SlotMap, E87Codes.ContentMap, E87Codes.LocationId, E87Codes.PortId })]
    public readonly string CarrierTrans15 = "CarrierSMTrans15";

    [EventAttribut("载具槽图核对不过（#16，Host 取消）", Data = new[] { E87Codes.CarrierId, E87Codes.SlotMapStatus, E87Codes.LocationId, E87Codes.PortId, E87Codes.Reason })]
    public readonly string CarrierTrans16 = "CarrierSMTrans16";

    [EventAttribut("载具没取放（#17）", Data = new[] { E87Codes.CarrierId, E87Codes.AccessingStatus })]
    public readonly string CarrierTrans17 = "CarrierSMTrans17";

    [EventAttribut("载具开始取放（#18）", Data = new[] { E87Codes.CarrierId, E87Codes.AccessingStatus, E87Codes.LocationId, E87Codes.PortId })]
    public readonly string CarrierTrans18 = "CarrierSMTrans18";

    [EventAttribut("载具干完（#19）", Data = new[] { E87Codes.CarrierId, E87Codes.AccessingStatus, E87Codes.ContentMap, E87Codes.LocationId, E87Codes.PortId })]
    public readonly string CarrierTrans19 = "CarrierSMTrans19";

    [EventAttribut("载具中断（#20）", Data = new[] { E87Codes.CarrierId, E87Codes.AccessingStatus, E87Codes.LocationId, E87Codes.PortId })]
    public readonly string CarrierTrans20 = "CarrierSMTrans20";

    [EventAttribut("载具对象删了（#21：载具取走、Host CarrierReCreate）", Data = new[] { E87Codes.CarrierId, E87Codes.LocationId, E87Codes.PortId })]
    public readonly string CarrierTrans21 = "CarrierSMTrans21";

    #endregion

    #region 事件：端口搬运状态（E87 Load Port Transfer 状态机 #1~#9）

    [EventAttribut("端口搬运状态初始（#1）", Data = new[] { E87Codes.PortId, E87Codes.TransferState })]
    public readonly string PortTrans01 = "PortTransferSMTrans01";

    [EventAttribut("端口启用（#2）", Data = new[] { E87Codes.PortId, E87Codes.TransferState })]
    public readonly string PortTrans02 = "PortTransferSMTrans02";

    [EventAttribut("端口停用（#3）", Data = new[] { E87Codes.PortId, E87Codes.TransferState })]
    public readonly string PortTrans03 = "PortTransferSMTrans03";

    [EventAttribut("端口启用后进挡着 / 可交接（#4）", Data = new[] { E87Codes.PortId, E87Codes.TransferState })]
    public readonly string PortTrans04 = "PortTransferSMTrans04";

    [EventAttribut("端口可交接后进等送 / 等取（#5）", Data = new[] { E87Codes.PortId, E87Codes.TransferState, E87Codes.CarrierId })]
    public readonly string PortTrans05 = "PortTransferSMTrans05";

    [EventAttribut("端口等送 → 挡着（#6，载具放上来）", Data = new[] { E87Codes.PortId, E87Codes.TransferState })]
    public readonly string PortTrans06 = "PortTransferSMTrans06";

    [EventAttribut("端口等取 → 挡着（#7，开始取走）", Data = new[] { E87Codes.PortId, E87Codes.TransferState })]
    public readonly string PortTrans07 = "PortTransferSMTrans07";

    [EventAttribut("端口挡着 → 等送（#8，载具取走了）", Data = new[] { E87Codes.PortId, E87Codes.TransferState })]
    public readonly string PortTrans08 = "PortTransferSMTrans08";

    [EventAttribut("端口挡着 → 等取（#9，载具卸好了）", Data = new[] { E87Codes.PortId, E87Codes.TransferState, E87Codes.CarrierId })]
    public readonly string PortTrans09 = "PortTransferSMTrans09";

    #endregion

    #region 事件：存取方式、关联、其它

    [EventAttribut("端口转自动存取（AMHS）", Data = new[] { E87Codes.PortId, E87Codes.AccessMode })]
    public readonly string AccessGoAuto = E87Codes.AccessGoAuto;

    [EventAttribut("端口转手动存取", Data = new[] { E87Codes.PortId, E87Codes.AccessMode })]
    public readonly string AccessGoManual = E87Codes.AccessGoManual;

    [EventAttribut("端口关联了载具", Data = new[] { E87Codes.PortId, E87Codes.AssociationState, E87Codes.CarrierId })]
    public readonly string AssociationGo = E87Codes.AssociationGo;

    [EventAttribut("端口取消关联", Data = new[] { E87Codes.PortId, E87Codes.AssociationState })]
    public readonly string AssociationGoNot = E87Codes.AssociationGoNot;

    [EventAttribut("读码失败（等 Host 带端口号给号或取消）", Data = new[] { E87Codes.PortId })]
    public readonly string CarrierIdReadFailEvent = E87Codes.CarrierIdReadFail;

    [EventAttribut("载具门打开（Load 好了）", Data = new[] { E87Codes.CarrierId, E87Codes.PortId })]
    public readonly string CarrierOpenedEvent = E87Codes.CarrierOpened;

    [EventAttribut("载具门关上（Unload 好了）", Data = new[] { E87Codes.CarrierId, E87Codes.PortId })]
    public readonly string CarrierClosedEvent = E87Codes.CarrierClosed;

    [EventAttribut("载具放到端口上（GEM Material Received）", Data = new[] { E87Codes.PortId })]
    public readonly string MaterialReceivedEvent = E87Codes.MaterialReceived;

    [EventAttribut("载具从端口拿走（GEM Material Removed）", Data = new[] { E87Codes.PortId, E87Codes.CarrierId })]
    public readonly string MaterialRemovedEvent = E87Codes.MaterialRemoved;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "10000", @default: "500",
        description: "多久查一次端口状态：端口忙闲、报警这些没有回调，搬运状态的变化靠它发现")]
    public int PortPollMs
    {
        get { return GetEcInt(nameof(PortPollMs)); }
        set { SetEcInt(nameof(PortPollMs), value); }
    }

    #endregion

    private readonly List<E87PortDevice> _ports = [];
    private E87Engine? _engine;

    #region 接设备

    /// <summary>
    /// 接到链路和设备上（EAP 元件在链路打开之前调）：每个 LoadPort 包成一个端口（PortID 按给的先后编），挂上 E87 回调和 E84 反查口，
    /// 建 E87 的协议部分（登记 S3 处理方，按 EC PortPollMs 查端口状态）；substrates 是槽图认定（料到了）时通知的 E90
    /// </summary>
    public void Attach(IHsms link, IE30Element gem, IReadOnlyList<ILoadPortAccess> ports, IE90Element? substrates)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        var engine = gem.Engine;
        if (engine is null)
        {
            throw new InvalidOperationException($"{Name}：E30 还没接链路");
        }

        var ledger = IocHelper.GetService<ILedgerManager>();
        foreach (var port in ports)
        {
            _ports.Add(new E87PortDevice(port, ledger));
        }

        _engine = new E87Engine(link.Link, engine, _ports, substrates?.Engine, this, this, new SecsLog(this), Name);
        foreach (var port in ports)
        {
            port.E87Callback = this;
            port.E84Provider = this;
        }
    }

    /// <summary>
    /// 从设备上摘下来（宿主退出时）
    /// </summary>
    public void Detach()
    {
        _engine?.Stop();
        _engine = null;
        foreach (var port in _ports)
        {
            port.LoadPort.E87Callback = null;
            port.LoadPort.E84Provider = null;
        }
    }

    private E87PortDevice? PortOf(ILoadPortAccess device)
    {
        return _ports.FirstOrDefault(port => ReferenceEquals(port.LoadPort, device));
    }

    /// <summary>
    /// LoadPort 报上来的事交给 E87（找不到端口、没接的不理）
    /// </summary>
    private void OnPort(ILoadPortAccess device, Action<E87Engine, IE87Port> work)
    {
        var engine = _engine;
        var port = PortOf(device);
        if (engine is null || port is null)
        {
            return;
        }

        work(engine, port);
    }

    #endregion

    #region 设备回调（IE87Callback，在 EAP 的上报派发线程上）

    void IE87Callback.CarrierArrived(ILoadPortAccess device)
    {
        OnPort(device, (engine, port) => engine.CarrierArrived(port));
    }

    void IE87Callback.CarrierIdRead(ILoadPortAccess device, string carrierId)
    {
        OnPort(device, (engine, port) => engine.CarrierIdRead(port, carrierId));
    }

    void IE87Callback.CarrierIdReadFailed(ILoadPortAccess device)
    {
        OnPort(device, (engine, port) => engine.CarrierIdReadFailed(port));
    }

    void IE87Callback.SlotMapRead(ILoadPortAccess device, IReadOnlyList<SlotState> slotMap)
    {
        OnPort(device, (engine, port) => engine.SlotMapRead(port));
    }

    void IE87Callback.LoadCompleted(ILoadPortAccess device)
    {
        OnPort(device, (engine, port) => engine.LoadCompleted(port));
    }

    void IE87Callback.UnloadCompleted(ILoadPortAccess device)
    {
        OnPort(device, (engine, port) => engine.UnloadCompleted(port));
    }

    void IE87Callback.AutoModeChanged(ILoadPortAccess device, bool autoMode)
    {
        OnPort(device, (engine, port) => engine.AutoModeChanged(port, autoMode));
    }

    void IE87Callback.AccessStarted(ILoadPortAccess device)
    {
        OnPort(device, (engine, port) => engine.AccessStarted(port));
    }

    void IE87Callback.AccessStopped(ILoadPortAccess device)
    {
        OnPort(device, (engine, port) => engine.AccessStopped(port));
    }

    void IE87Callback.CarrierComplete(ILoadPortAccess device)
    {
        OnPort(device, (engine, port) => engine.CarrierComplete(port));
    }

    void IE87Callback.PortError(ILoadPortAccess device, string error)
    {
        OnPort(device, (engine, port) => engine.PortError(port));
    }

    void IE87Callback.CarrierRemoved(ILoadPortAccess device, string? carrierId)
    {
        OnPort(device, (engine, port) => engine.CarrierRemoved(port, carrierId));
    }

    void IGemEvents.Report(string code, params (string Name, object? Value)[] data)
    {
        RaiseEvent(code, data.Select(item => new GemData(item.Name, item.Value)).ToArray());
    }

    #endregion

    #region E84 反查（E84 每一拍问一次端口能不能交接）

    LoadPortTransferState IE84Provider.GetTransferState(ILoadPortAccess device)
    {
        var engine = _engine;
        var port = PortOf(device);
        if (engine is null || port is null)
        {
            return device.LocalTransferState;
        }

        if (device.LocalTransferState == LoadPortTransferState.OutOfService)
        {
            return LoadPortTransferState.OutOfService;
        }

        switch (engine.TransferState(port))
        {
            case E87TransferState.OutOfService:
                return LoadPortTransferState.OutOfService;
            case E87TransferState.ReadyToLoad:
                return LoadPortTransferState.ReadyToLoad;
            case E87TransferState.ReadyToUnload:
                return LoadPortTransferState.ReadyToUnload;
            case E87TransferState.NoState:
                return device.LocalTransferState;
            default:
                return LoadPortTransferState.TransferBlocked;
        }
    }

    bool IE84Provider.IsAutoAccessMode(ILoadPortAccess device)
    {
        return device.IsAutoMode;
    }

    #endregion
}
