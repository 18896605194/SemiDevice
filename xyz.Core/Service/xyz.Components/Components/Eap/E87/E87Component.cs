using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Drivers.Loadport;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// 载具管理
/// </summary>
[Component(description: "载具管理（SEMI E87）：载具 ID / 槽图跟 Host 核对、端口搬运状态，S3 报文")]
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
    private const string DvLocationId = "LocationID";
    private const string DvTransferState = "PortTransferState";
    private const string DvAccessMode = "PortAccessMode";
    private const string DvAssociationState = "PortAssociationState";
    private const string DvReason = "Reason";

    [DataVariable(ValueFormat.String, "载具号")]
    public readonly string CarrierIdData = DvCarrierId;

    [DataVariable(ValueFormat.Int, "端口号（PortID，按 LoadPort 先后从 1 开始）")]
    public readonly string PortIdData = DvPortId;

    [DataVariable(ValueFormat.Int, "载具 ID 状态：1 等 Host、2 认定、3 核对不过")]
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

    [DataVariable(ValueFormat.String, "载具在哪（端口名）")]
    public readonly string LocationIdData = DvLocationId;

    [DataVariable(ValueFormat.Int, "端口搬运状态：0 停用、1 挡着、2 等送、3 等取")]
    public readonly string TransferStateData = DvTransferState;

    [DataVariable(ValueFormat.Int, "端口存取方式：0 手动、1 自动")]
    public readonly string AccessModeData = DvAccessMode;

    [DataVariable(ValueFormat.Int, "端口关联：0 没关联、1 关联了载具")]
    public readonly string AssociationStateData = DvAssociationState;

    [DataVariable(ValueFormat.Int, "原因：0 等 Host 核对槽图、5 Host 取消")]
    public readonly string ReasonData = DvReason;

    #endregion

    #region 事件：载具（E87 Carrier 状态机，由载具 ID、槽图、取放三个状态机报）

    [EventAttribut("载具对象建了（#1）", Data = new[] { DvCarrierId, DvLocationId })]
    public readonly string CarrierTrans01 = "CarrierSMTrans01";

    [EventAttribut("载具 ID 等 Host 核对（#3）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId })]
    public readonly string CarrierTrans03 = "CarrierSMTrans03";

    [EventAttribut("载具 ID 认定（#4，读码失败、Host 给号）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId })]
    public readonly string CarrierTrans04 = "CarrierSMTrans04";

    [EventAttribut("载具 ID 认定（#8，Host 让继续）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId })]
    public readonly string CarrierTrans08 = "CarrierSMTrans08";

    [EventAttribut("载具 ID 核对不过（#9，Host 取消）", Data = new[] { DvCarrierId, DvCarrierIdStatus, DvLocationId, DvPortId, DvReason })]
    public readonly string CarrierTrans09 = "CarrierSMTrans09";

    [EventAttribut("载具槽图没读（#12）", Data = new[] { DvCarrierId, DvSlotMapStatus })]
    public readonly string CarrierTrans12 = "CarrierSMTrans12";

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

    [EventAttribut("载具对象删了（#21：载具取走、Host CarrierReCreate）", Data = new[] { DvCarrierId, DvLocationId, DvPortId })]
    public readonly string CarrierTrans21 = "CarrierSMTrans21";

    #endregion

    #region 事件：端口搬运状态（E87 Load Port Transfer 状态机 #1~#9）

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

    #endregion

    #region 事件：存取方式、关联、其它

    [EventAttribut("端口转自动存取（AMHS）", Data = new[] { DvPortId, DvAccessMode })]
    public readonly string AccessGoAuto = "AccessSMGoAuto";

    [EventAttribut("端口转手动存取", Data = new[] { DvPortId, DvAccessMode })]
    public readonly string AccessGoManual = "AccessSMGoManual";

    [EventAttribut("端口关联了载具", Data = new[] { DvPortId, DvAssociationState, DvCarrierId })]
    public readonly string AssociationGo = "AssocSMGoAssoc";

    [EventAttribut("端口取消关联", Data = new[] { DvPortId, DvAssociationState })]
    public readonly string AssociationGoNot = "AssocSMGoNotAssoc";

    [EventAttribut("读码失败（等 Host 带端口号给号或取消）", Data = new[] { DvPortId })]
    public readonly string CarrierIdReadFailEvent = "CarrierIDReadFail";

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

    [SCEditor("True", "E87", "载具干完或中断后自动 Unload（关门、松开），端口转等取；False = 等 Host CarrierRelease 或操作员点 Unload")]
    public bool AutoUnload { get; set; } = true;

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "10000", @default: "500",
        description: "多久查一次端口状态：端口忙闲、报警这些没有回调，搬运状态的变化靠它发现")]
    public int PortPollMs
    {
        get { return GetEcInt(nameof(PortPollMs)); }
        set { SetEcInt(nameof(PortPollMs), value); }
    }

    #endregion

    private readonly object _gate = new();
    private readonly List<E87Port> _ports = [];
    private readonly List<Action> _later = [];
    private E30Component? _gem;
    private Action<string>? _materialVerified;
    private Timer? _poll;

    #region 接设备

    /// <summary>
    /// 接到链路和设备上（EAP 组件在链路打开之前调）：每个 LoadPort 建一个端口对象（PortID 按给的先后编），挂上 E87 回调和 E84 反查口，
    /// 登记 S3 处理方；materialVerified 是槽图认定（料到了）时通知的（E90 据此建片对象）。开始按 EC PortPollMs 查端口状态。
    /// </summary>
    public void Attach(HsmsComponent link, E30Component gem, IReadOnlyList<ILoadPort> ports, Action<string>? materialVerified)
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
                _ports.Add(new E87Port(this, device, id++));
                device.E87Callback = this;
                device.E84Provider = this;
            }

            foreach (var port in _ports)
            {
                port.TransferMachine.Refresh();
            }
        }

        link.Handle(3, 15, Inquire);
        link.Handle(3, 17, CarrierAction);
        link.Handle(3, 25, PortAction);
        link.Handle(3, 27, ChangeAccess);
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
            Locked(() =>
            {
                foreach (var port in _ports)
                {
                    port.TransferMachine.Refresh();
                }
            });
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

    #region 设备回调（IE87Callback，在 EAP 的上报派发线程上）：翻成状态机的消息

    void IE87Callback.CarrierArrived(ILoadPort device)
    {
        OnPort(device, port =>
        {
            port.Released = false;
            ReportPort(MaterialReceivedEvent, port);
        });
    }

    void IE87Callback.CarrierIdRead(ILoadPort device, string carrierId)
    {
        OnPort(device, port =>
        {
            // 已经有载具对象（Host 先给了号、重复上报）的不理
            if (port.HasCarrier)
            {
                return;
            }

            var other = FindCarrier(carrierId);
            if (other is not null)
            {
                LogHelper.Warn(Name, $"端口 {port.Id} 读到的载具号 {carrierId} 端口 {other.Id} 上已有，不重复建对象");
                return;
            }

            CreateCarrier(port, carrierId, E87CarrierIdMessage.IdRead);
        });
    }

    void IE87Callback.CarrierIdReadFailed(ILoadPort device)
    {
        // 读不出号、也没有载具对象：报读码失败，等 Host 带端口号给号（ProceedWithCarrier）或取消
        OnPort(device, port =>
        {
            if (!port.HasCarrier)
            {
                ReportPort(CarrierIdReadFailEvent, port);
            }
        });
    }

    void IE87Callback.SlotMapRead(ILoadPort device, IReadOnlyList<SlotState> slotMap)
    {
        OnPort(device, port => port.SlotMapMachine.Post(E87SlotMapMessage.Read));
    }

    void IE87Callback.LoadCompleted(ILoadPort device)
    {
        OnPort(device, port => ReportDoor(CarrierOpenedEvent, port));
    }

    void IE87Callback.UnloadCompleted(ILoadPort device)
    {
        OnPort(device, port => ReportDoor(CarrierClosedEvent, port));
    }

    void IE87Callback.AutoModeChanged(ILoadPort device, bool autoMode)
    {
        OnPort(device, port => port.AccessModeMachine.Post(autoMode ? E87AccessModeMessage.GoAuto : E87AccessModeMessage.GoManual));
    }

    void IE87Callback.AccessStarted(ILoadPort device)
    {
        OnPort(device, port => port.AccessMachine.Post(E87AccessMessage.Start));
    }

    void IE87Callback.AccessStopped(ILoadPort device)
    {
        OnPort(device, port => port.AccessMachine.Post(E87AccessMessage.Stop));
    }

    void IE87Callback.CarrierComplete(ILoadPort device)
    {
        OnPort(device, port =>
        {
            // 没报过开始取放的先补一个，再报干完
            port.AccessMachine.Post(E87AccessMessage.Start);
            port.AccessMachine.Post(E87AccessMessage.Complete);
        });
    }

    void IE87Callback.PortError(ILoadPort device, string error)
    {
        // 取放途中出错：设备侧已经把载具记成中断，这边跟着报 #20
        OnPort(device, port => port.AccessMachine.Post(E87AccessMessage.Stop));
    }

    void IE87Callback.CarrierRemoved(ILoadPort device, string? carrierId)
    {
        OnPort(device, port =>
        {
            string removedId = port.HasCarrier ? port.CarrierId : carrierId ?? string.Empty;
            DeleteCarrier(port);
            port.Released = false;
            Report(MaterialRemovedEvent, new GemData(DvPortId, port.Id), new GemData(DvCarrierId, GemValue.Ascii(removedId)));
        });
    }

    /// <summary>回调的公共部分：在锁里找到端口、做事，最后按设备现在的样子刷一下搬运状态。</summary>
    private void OnPort(ILoadPort device, Action<E87Port> work)
    {
        Locked(() =>
        {
            var port = PortOf(device);
            if (port is null)
            {
                return;
            }

            work(port);
            port.TransferMachine.Refresh();
        });
    }

    #endregion

    #region 载具对象（在锁里调）

    /// <summary>
    /// 建载具对象：记下号，ID 状态机按怎么来的转（读到号等 Host #1、#3；Host 给号直接认定 #1、#4），槽图进没读（#12）、取放进没取放（#17），端口关联。
    /// </summary>
    private void CreateCarrier(E87Port port, string carrierId, E87CarrierIdMessage how)
    {
        port.CarrierId = carrierId;
        port.CarrierIdMachine.Post(how);
        port.SlotMapMachine.Post(E87SlotMapMessage.Create);
        port.AccessMachine.Post(E87AccessMessage.Create);
        port.AssociationMachine.Post(E87AssociationMessage.Associate);
    }

    /// <summary>删载具对象（#21）：几个状态机回到没有载具，取消关联。</summary>
    private void DeleteCarrier(E87Port port)
    {
        port.CarrierIdMachine.Post(E87CarrierIdMessage.Delete);
        port.SlotMapMachine.Post(E87SlotMapMessage.Delete);
        port.AccessMachine.Post(E87AccessMessage.Delete);
        port.AssociationMachine.Post(E87AssociationMessage.Dissociate);
        port.CarrierId = string.Empty;
    }

    /// <summary>按载具号找它在哪个端口上；没有为 null。</summary>
    private E87Port? FindCarrier(string carrierId)
    {
        return _ports.FirstOrDefault(port => port.HasCarrier && string.Equals(port.CarrierId, carrierId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>槽图认定了（料到了）：出锁后通知 E90 建片对象。</summary>
    internal void NotifyMaterialVerifiedLater(E87Port port)
    {
        string name = port.Device.Name;
        var notify = _materialVerified;
        Later(() => notify?.Invoke(name));
    }

    #endregion

    #region 锁和设备动作

    /// <summary>攒一个设备动作，出锁再做（只在锁里调）：动作发起会拿模块的锁，别跟这边的锁绞在一起。</summary>
    internal void Later(Action action)
    {
        _later.Add(action);
    }

    /// <summary>在锁里改状态，攒下的设备动作出锁再做。</summary>
    private void Locked(Action work)
    {
        Locked(() =>
        {
            work();
            return true;
        });
    }

    private T Locked<T>(Func<T> work)
    {
        T result;
        List<Action> later;
        lock (_gate)
        {
            try
            {
                result = work();
            }
            finally
            {
                later = _later.ToList();
                _later.Clear();
            }
        }

        foreach (var action in later)
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

        return result;
    }

    #endregion

    #region E84 反查（E84 每一拍问一次端口能不能交接）

    LoadPortTransferState IE84Provider.GetTransferState(ILoadPort device)
    {
        lock (_gate)
        {
            var port = PortOf(device);
            if (port is null)
            {
                return device.LocalTransferState;
            }

            if (device.LocalTransferState == LoadPortTransferState.OutOfService)
            {
                return LoadPortTransferState.OutOfService;
            }

            var state = port.TransferMachine.Compute();
            if (state == E87TransferState.OutOfService)
            {
                return LoadPortTransferState.OutOfService;
            }

            if (state == E87TransferState.ReadyToLoad)
            {
                return LoadPortTransferState.ReadyToLoad;
            }

            if (state == E87TransferState.ReadyToUnload)
            {
                return LoadPortTransferState.ReadyToUnload;
            }

            return LoadPortTransferState.TransferBlocked;
        }
    }

    bool IE84Provider.IsAutoAccessMode(ILoadPort device)
    {
        return device.IsAutoMode;
    }

    #endregion

    #region 报事件（状态机进状态时调）

    internal void ReportPort(string code, E87Port port)
    {
        Report(code, PortData(port).Append(new GemData(DvCarrierId, GemValue.Ascii(port.CarrierId))).ToArray());
    }

    internal void ReportCarrier(string code, E87Port port, byte? reason = null)
    {
        var data = CarrierData(port).Concat(PortData(port)).ToList();
        if (reason is not null)
        {
            data.Add(new GemData(DvReason, reason.Value));
        }

        Report(code, data.ToArray());
    }

    /// <summary>开门、关门：载具号没有 E87 对象时用设备读到的。</summary>
    private void ReportDoor(string code, E87Port port)
    {
        string carrierId = port.HasCarrier ? port.CarrierId : port.Device.CarrierId ?? string.Empty;
        Report(code, new GemData(DvCarrierId, GemValue.Ascii(carrierId)), new GemData(DvPortId, port.Id));
    }

    private void Report(string code, params GemData[] data)
    {
        _gem?.Report(this, code, data);
    }

    private static IEnumerable<GemData> PortData(E87Port port)
    {
        var transfer = port.TransferMachine.State;
        yield return new GemData(DvPortId, port.Id);
        yield return new GemData(DvTransferState, (byte)(transfer == E87TransferState.NoState ? E87TransferState.TransferBlocked : transfer));
        yield return new GemData(DvAccessMode, (byte)port.AccessModeMachine.State);
        yield return new GemData(DvAssociationState, (byte)port.AssociationMachine.State);
    }

    /// <summary>
    /// 载具的数据：几个状态取状态机的（没有载具报空 U1）；槽数、槽图、片数直接问 LoadPort；片号表槽图认定后按晶圆账报。
    /// </summary>
    private static IEnumerable<GemData> CarrierData(E87Port port)
    {
        var device = port.Device;
        var slots = device.SlotMap;
        yield return new GemData(DvCarrierId, GemValue.Ascii(port.CarrierId));
        yield return new GemData(DvCarrierIdStatus, StatusItem((byte)port.CarrierIdMachine.State));
        yield return new GemData(DvSlotMapStatus, StatusItem((byte)port.SlotMapMachine.State));
        yield return new GemData(DvAccessingStatus, StatusItem((byte)port.AccessMachine.State));
        yield return new GemData(DvCapacity, SecsItem.U1((byte)Math.Min(byte.MaxValue, device.SlotCount)));
        yield return new GemData(DvSubstrateCount, slots.Count == 0
            ? SecsItem.U1()
            : SecsItem.U1((byte)slots.Count(slot => slot is SlotState.CorrectlyOccupied or SlotState.NotEmpty)));
        yield return new GemData(DvSlotMap, SecsItem.L(slots.Select(slot => SecsItem.U1((byte)slot))));
        yield return new GemData(DvContentMap, ContentMapItem(port));
        yield return new GemData(DvLocationId, GemValue.Ascii(device.Name));
    }

    /// <summary>状态值：没有载具（255）报空 U1。</summary>
    private static SecsItem StatusItem(byte state)
    {
        return state == byte.MaxValue ? SecsItem.U1() : SecsItem.U1(state);
    }

    /// <summary>片号表 L[槽数]{L[2]{批次号, 片号}}：槽图认定后按晶圆账报（Host 给的片号已经写进账了）；没认定报空表。</summary>
    private static SecsItem ContentMapItem(E87Port port)
    {
        var ledger = WaferManagerComponent.Current;
        if (port.SlotMapMachine.State != E87SlotMapState.Verified || ledger is null)
        {
            return SecsItem.L();
        }

        return SecsItem.L(ledger.GetSlots(port.Device.Name).Select(wafer => SecsItem.L(
            SecsItem.A(GemValue.Ascii(wafer?.LotId ?? string.Empty)), SecsItem.A(GemValue.Ascii(wafer?.WaferId ?? string.Empty)))));
    }

    #endregion
}
