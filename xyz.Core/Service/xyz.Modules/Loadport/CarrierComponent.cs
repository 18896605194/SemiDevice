using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Drivers.Loadport;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 载具（FOUP）
/// </summary>
[Component(description: "载具（FOUP）：到达、读码、槽图、取放状态，向 E87 上报")]
public class CarrierComponent : ComponentBase, ICarrier
{
    #region SC

    [SCEditor("Query", "_carrier",
        "载具在位以什么为准：Query = 状态查询（在位、到位两位都亮算放好，都灭算拿走，一亮一灭不算变化）；Event = 设备主动上报（PODON 放上 / PODOF 拿走）")]
    public PodPresenceSource PresenceSource { get; set; } = PodPresenceSource.Query;

    [SCEditor("True", "_carrier", "载具到位后自动读码（False=只由上层/EAP 显式触发）")]
    public bool AutoReadCarrierId { get; set; } = true;

    #endregion

    #region SV

    private volatile bool _isArrived;

    /// <summary>
    /// 载具到了（SV）：按 SC PresenceSource 判出来的结果，不是哪个传感器的原始值——Query 看状态查询的在位（IsPresent）、
    /// 到位（IsPlaced）两位，Event 看 PODON/PODOF；只在端口的扫描线程上改。载具到达、拿走、E84 交接、Job、回片、E87 用的都是它，
    /// 推给界面的"在位"也是它。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "载具到了（在位、到位都亮，或设备报了放上）")]
    public bool IsArrived
    {
        get => _isArrived;
        private set => _isArrived = value;
    }

    private volatile string? _carrierId;

    /// <summary>载具 ID。</summary>
    [VariableMark(VariableType.SV, ValueFormat.String, description: "当前载具 ID")]
    public string? CarrierId
    {
        get => _carrierId;
        private set => _carrierId = value;
    }

    private volatile IReadOnlyList<SlotState> _slotMap = Array.Empty<SlotState>();

    /// <summary>
    /// 最近一次 Mapping 结果，下标 0 对应第 1 槽；未 Mapping 或载具已移走为空列表。
    /// </summary>
    public IReadOnlyList<SlotState> SlotMap
    {
        get => _slotMap;
        private set => _slotMap = value;
    }

    #endregion

    #region Event

    [EventAttribut("FOUP 到达", Description = "FOUP 从不在位变为在位")]
    public readonly string FoupArrivedEvent = "FoupArrived";

    [EventAttribut("FOUP 移除", Description = "FOUP 从在位变为不在位")]
    public readonly string FoupRemovedEvent = "FoupRemoved";

    #endregion

    private readonly object _gate = new();
    private volatile CarrierInfo? _info;

    /// <summary>设备最近一次主动报的是放上（PODON）还是拿走（PODOF）；只是记下，算不算到达由扫描线程按 PresenceSource 判。</summary>
    private volatile bool _deviceReportedPlaced;

    private ILoadPort? _port;

    /// <summary>读码器（端口下的兄弟节点，端口挂载时交过来）；没配为 null，这个端口不读码，ID 由 Host 给。</summary>
    private ICarrierIdReader? _reader;

    /// <summary>晶圆账：组件初始化时取一次；没配晶圆账、或端口停用（不给子组件做初始化）为 null。</summary>
    private WaferManagerComponent? _waferManager;

    private Action<Action<IE87Callback>>? _enqueue;

    /// <summary>到位后要自动读码，但读头这会儿没发起成功（没连上、或上一次还没读完）：下一拍接着试。只在扫描线程上读写。</summary>
    private bool _autoReadPending;

    /// <summary>从到位、发现没发起成功起算，等读头的时间；超过读头的读码超时（EC ReadCarrierIdTimeout）就当读码失败。</summary>
    private readonly Stopwatch _autoReadWatch = new();

    /// <summary>
    /// 这一盒载具的快照（记录类型，改的时候整个换掉，读的人拿到的不会半截变）；端口上没载具为 null。
    /// </summary>
    public CarrierInfo? Info => _info;

    /// <summary>
    /// 载具这边认可了，能取放片：没接 EAP 没人核对，读到就算；接了 EAP 槽图要被 Host 认定（E87 写回 Verified）才行，
    /// 免得 Host 还没核对完、或者核对不过就已经取走了。门开没开是端口的事，端口的 CanAssignCarrierToJob（排活）、CanPrepare（机械手进站）再加上它。
    /// </summary>
    public bool IsAccepted
    {
        get
        {
            var port = _port;
            if (port is null)
            {
                return false;
            }

            if (port.E87Callback is null)
            {
                return true;
            }

            var info = _info;
            return info is not null && info.SlotMapStatus == CarrierSlotMapStatus.Verified;
        }
    }

    private ILoadPort Port
    {
        get
        {
            var port = _port;
            if (port is null)
            {
                throw new InvalidOperationException($"载具组件 {FullPath} 还没挂到 LoadPort 上（Attach）。");
            }

            return port;
        }
    }

    #region 端口调的口（只有 LoadPort 模块调，EAP 别调）

    /// <summary>
    /// 端口把自己、读码器（没配为 null）和 E87 上报的入队口交给它（载具没有父引用，跟 E84 一样由端口驱动）。可以重复调。
    /// 上报都放进端口给的入队口，跟端口自己的上报走同一条线，先后不乱。
    /// </summary>
    public void Attach(ILoadPort port, ICarrierIdReader? reader, Action<Action<IE87Callback>> enqueue)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(enqueue);
        _port = port;
        _reader = reader;
        _enqueue = enqueue;
    }

    /// <summary>
    /// 每拍把状态查询的两个传感器位喂进来（null = 这次没查到）：Query 模式两位都亮算放好、都灭算拿走、一亮一灭或查不到保持原判断；
    /// Event 模式只认 PODON/PODOF（<see cref="SetDeviceReportedPlaced"/>），传感器位不管。判出变化就建 / 清载具并上报，只在端口的扫描线程上调。
    /// </summary>
    public void Sense(bool? isPresent, bool? isPlaced)
    {
        bool arrived = Judge(isPresent, isPlaced);
        if (arrived == IsArrived)
        {
            return;
        }

        IsArrived = arrived;
        if (arrived)
        {
            Arrive();
            return;
        }

        Remove();
    }

  
    public void SetDeviceReportedPlaced(bool placed)
    {
        _deviceReportedPlaced = placed;
    }

    /// <summary>
    /// Load 带回了 Mapping 结果：更新槽图、整篮落晶圆账、报 E87 SlotMapRead；空列表忽略。
    /// 槽图认定状态只往前走：还没读过才转 Read，Host 已经认定（或在等、或判了不过）的，再 Map 一次只更新槽图和账、不动认定状态；
    /// 重置只靠载具拿走或 Host 的 CarrierReCreate。
    /// </summary>
    public void UpdateSlotMap(IReadOnlyList<SlotState> slotMap)
    {
        ArgumentNullException.ThrowIfNull(slotMap);
        if (slotMap.Count == 0)
        {
            return;
        }

        var snapshot = slotMap.ToArray();
        SlotMap = snapshot;
        UpdateInfo(info => info.SlotMapStatus == CarrierSlotMapStatus.NotRead
            ? info with { SlotMapStatus = CarrierSlotMapStatus.Read }
            : info);
        ApplySlotMapToLedger(snapshot);
        NotifyE87((callback, port) => callback.SlotMapRead(port, snapshot));
    }

    /// <summary>
    /// Load 好了：这个载具进 E87 的 IN ACCESS（照老 CTC，Load 好就算开始取放），报 AccessStarted；已经取放过的不动。
    /// </summary>
    public void StartAccess()
    {
        bool started = false;
        lock (_gate)
        {
            var info = _info;
            if (info is not null && info.AccessStatus == CarrierAccessStatus.NotAccessed)
            {
                _info = info with { AccessStatus = CarrierAccessStatus.InAccess, UpdatedAt = DateTime.Now };
                started = true;
            }
        }

        if (started)
        {
            NotifyE87((callback, port) => callback.AccessStarted(port));
        }
    }

    /// <summary>
    /// Unload 好了，取放结束：取放过、没判完成的记中断（E87 CARRIER STOPPED），已经 Complete / Stopped 的保持原样；报 AccessStopped。
    /// 干完没干完不由这里判——上层作业调 <see cref="NoteComplete"/> 才算完成。
    /// </summary>
    public void EndAccess()
    {
        MarkStopped();
        NotifyE87((callback, port) => callback.AccessStopped(port));
    }

    /// <summary>
    /// 端口的动作没做成（失败、超时、被顶替）：取放途中出错，这个载具算没干完，记中断；还没开始取放的不动它。
    /// 不报 AccessStopped——端口紧接着报 PortError，E87 据此转中断。
    /// </summary>
    public void MarkAccessStopped()
    {
        MarkStopped();
    }

    #endregion

    #region ICarrier

    public bool ReadId()
    {
        var reader = _reader;
        return reader is not null && reader.StartReadCarrierId();
    }

    public void SetId(string carrierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        CarrierId = carrierId;

        // Host 改写即认定：读到什么不重要了，以 Host 为准。
        UpdateInfo(info => info with { CarrierId = carrierId, IdStatus = CarrierIdStatus.Verified });
        _waferManager?.SetCarrierIdOn(Port.Name, carrierId);
    }

    public void UpdateStatus(CarrierIdStatus? idStatus, CarrierSlotMapStatus? slotMapStatus)
    {
        UpdateInfo(info => info with
        {
            IdStatus = idStatus ?? info.IdStatus,
            SlotMapStatus = slotMapStatus ?? info.SlotMapStatus,
        });
    }

    public void NoteComplete()
    {
        UpdateInfo(info => info with { AccessStatus = CarrierAccessStatus.Complete });
        NotifyE87((callback, port) => callback.CarrierComplete(port));
    }

    #endregion

    /// <summary>
    /// 组件初始化（开机，端口初始化时跟着递归进来）：晶圆账取一次存着，落账、补载具号、清账都用它。
    /// </summary>
    public override bool InitComponent()
    {
        _waferManager = WaferManagerComponent.Current;
        return base.InitComponent();
    }

    /// <summary>
    /// 扫描周期：先扫子组件，再收读头读码的结果。读头是端口下的兄弟节点、在 sc.xml 里排在本组件前面，
    /// 同一拍里它先出结果、这里紧跟着取。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        TakeReadResult();
        RetryAutoRead();
    }

    /// <summary>
    /// 收读码结果：成功更新载具号并报 CarrierIdRead，失败报 CarrierIdReadFailed。
    /// </summary>
    private void TakeReadResult()
    {
        var result = _reader?.GetCarrierIdResult();
        if (result is null)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            LogHelper.Warn(Port.Name, $"读码失败: {result.ErrorMessage}");
            UpdateInfo(info => info with { IdStatus = CarrierIdStatus.ReadFailed });
            NotifyE87((callback, port) => callback.CarrierIdReadFailed(port));
            return;
        }

        string carrierId = result.Result ?? string.Empty;
        CarrierId = carrierId;

        // 读到 ≠ 认定：接了 EAP 的话还要 Host 点头（ProceedWithCarrier）才转 Verified。
        UpdateInfo(info => info with { CarrierId = carrierId, IdStatus = CarrierIdStatus.Read });
        _waferManager?.SetCarrierIdOn(Port.Name, carrierId);
        NotifyE87((callback, port) => callback.CarrierIdRead(port, carrierId));
    }

    /// <summary>
    /// 这一拍载具到没到：Event 只看 PODON/PODOF；Query 看两位传感器，都亮到了、都灭走了，一亮一灭或查不到保持原判断。
    /// </summary>
    private bool Judge(bool? isPresent, bool? isPlaced)
    {
        if (PresenceSource == PodPresenceSource.Event)
        {
            return _deviceReportedPlaced;
        }

        if (isPresent is null || isPlaced is null)
        {
            return IsArrived;
        }

        if (isPresent.Value && isPlaced.Value)
        {
            return true;
        }

        if (!isPresent.Value && !isPlaced.Value)
        {
            return false;
        }

        return IsArrived;
    }

    /// <summary>
    /// 载具放上来了：建载具对象，报 E87 CarrierArrived 和 FOUP 到达事件，该自动读码就发起读码。
    /// </summary>
    private void Arrive()
    {
        var port = Port;
        var info = new CarrierInfo { Location = port.Name, Capacity = port.SlotCount };
        lock (_gate)
        {
            _info = info;
        }

        NotifyE87((callback, target) => callback.CarrierArrived(target));
        RaiseEvent(FoupArrivedEvent);
        if (AutoReadCarrierId)
        {
            StartAutoRead();
        }
    }

    /// <summary>
    /// 到位后的自动读码。没配读头的不算失败（这个端口本来就不读码，ID 由 Host 给）；读头没发起成功（读头断线重连中、
    /// 上一次还没读完）不能就这么算了——记下来，之后每拍接着试（<see cref="RetryAutoRead"/>），免得 Host 干等一个永远不来的 ID。
    /// </summary>
    private void StartAutoRead()
    {
        var reader = _reader;
        if (reader is null || reader.StartReadCarrierId())
        {
            return;
        }

        _autoReadPending = true;
        _autoReadWatch.Restart();
    }

    /// <summary>
    /// 自动读码没发起成功的接着试：发起了就交给 <see cref="TakeReadResult"/> 收结果；载具走了、已经有读码结果（别处读过了）就不试了；
    /// 试到读头的读码超时（EC ReadCarrierIdTimeout）还没发起成功，按读码失败报给 E87，Host 就能带端口号给号或取消。
    /// </summary>
    private void RetryAutoRead()
    {
        if (!_autoReadPending)
        {
            return;
        }

        var info = _info;
        if (!IsArrived || info is null || info.IdStatus != CarrierIdStatus.NotRead)
        {
            _autoReadPending = false;
            return;
        }

        var reader = _reader;
        if (reader is null)
        {
            _autoReadPending = false;
            return;
        }

        if (reader.StartReadCarrierId())
        {
            _autoReadPending = false;
            return;
        }

        int timeout = reader.ReadCarrierIdTimeout;
        if (_autoReadWatch.ElapsedMilliseconds < timeout)
        {
            return;
        }

        _autoReadPending = false;
        LogHelper.Warn(Port.Name, $"读头一直没能发起读码（{timeout}ms 内没连上或一直在忙），按读码失败处理");
        UpdateInfo(current => current with { IdStatus = CarrierIdStatus.ReadFailed });
        NotifyE87((callback, port) => callback.CarrierIdReadFailed(port));
    }

    /// <summary>
    /// 载具拿走了：清载具对象、载具号、槽图，这个端口上的片也一起走（晶圆账上清掉，免得留一堆幽灵片），报 E87 CarrierRemoved 和 FOUP 移除事件。
    /// </summary>
    private void Remove()
    {
        string? carrierId = CarrierId;
        CarrierId = null;
        SlotMap = Array.Empty<SlotState>();
        _autoReadPending = false;
        lock (_gate)
        {
            _info = null;
        }

        _waferManager?.Clear(Port.Name);
        NotifyE87((callback, port) => callback.CarrierRemoved(port, carrierId));
        RaiseEvent(FoupRemovedEvent);
    }

    /// <summary>
    /// 取放过、没判完成的记中断；已经 Complete / Stopped 的保持原样。
    /// </summary>
    private void MarkStopped()
    {
        UpdateInfo(info => info.AccessStatus == CarrierAccessStatus.InAccess
            ? info with { AccessStatus = CarrierAccessStatus.Stopped }
            : info);
    }

    /// <summary>
    /// 改载具快照（锁里整个换掉）；没有载具时什么都不做。
    /// </summary>
    private void UpdateInfo(Func<CarrierInfo, CarrierInfo> change)
    {
        lock (_gate)
        {
            var info = _info;
            if (info is null)
            {
                return;
            }

            _info = change(info) with { UpdatedAt = DateTime.Now };
        }
    }

    /// <summary>
    /// 把 Mapping 结果落到晶圆账：这个端口的账整篮重建，一槽一片。
    ///
    /// 识别不出来的槽（Undefined）按"有片但状态不明"记，不按空槽记——两种错的代价不一样：
    /// 记成有片而实际没有，机械手去取会空手，Verify 对不上报警，停下来让人看；
    /// 记成空而实际有片，机械手会往上放，那是撞片。宁可多记不可漏记。
    /// </summary>
    private void ApplySlotMapToLedger(IReadOnlyList<SlotState> slotMap)
    {
        if (_waferManager is null)
        {
            return;
        }

        var port = Port;
        _waferManager.RegisterLocation(port.Name, port.SlotCount);

        var statuses = new WaferStatus?[slotMap.Count];
        for (int index = 0; index < slotMap.Count; index++)
        {
            statuses[index] = ToWaferStatus(slotMap[index]);
        }

        var info = _info;
        int created = _waferManager.ApplySlotMap(port.Name, statuses, info?.CarrierId, info?.LotId);
        LogHelper.Info($"[{port.Name}] Mapping 落账：{created} 片");
    }

    /// <summary>
    /// 槽位状态换成账上的片状态：空槽为 null（不建片）；认不出的按有片记 Unknown（见上面宁可多记不可漏记）。
    /// </summary>
    private static WaferStatus? ToWaferStatus(SlotState state)
    {
        switch (state)
        {
            case SlotState.Empty:
                return null;

            case SlotState.CorrectlyOccupied:
            case SlotState.NotEmpty:
                return WaferStatus.Normal;

            case SlotState.DoubleSlotted:
                return WaferStatus.Double;

            case SlotState.CrossSlotted:
                return WaferStatus.Crossed;

            default:
                return WaferStatus.Unknown;
        }
    }

    /// <summary>
    /// 一条 E87 上报放进端口给的入队口（跟端口自己的上报同一条线）；没挂 EAP 时入队口直接丢弃。任意线程可调。
    /// </summary>
    private void NotifyE87(Action<IE87Callback, ILoadPort> notification)
    {
        var port = _port;
        var enqueue = _enqueue;
        if (port is null || enqueue is null)
        {
            return;
        }

        enqueue(callback => notification(callback, port));
    }
}
