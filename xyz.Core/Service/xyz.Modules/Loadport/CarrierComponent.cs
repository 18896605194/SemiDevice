using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Drivers.Loadport;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

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
    /// 到位（IsPlaced）两位，Event 看 PODON/PODOF；只在端口的扫描线程上改。载具到达、拿走、E84 交接、Job、回片、E87 用的都是它。
    /// 界面不看它：三维图上的盒子跟原始两位走（两位都亮才画）。
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
 
    private ICarrierIdReader? _reader;

    private ILedgerManagerComponent? _ledger;

    private Action<Action<IE87Callback>>? _enqueue;

    /// <summary>
    /// 到位后要自动读码
    /// </summary>
    private bool _autoReadPending;

    /// <summary>从到位、发现没发起成功起算，等读头的时间；超过读头的读码超时（EC ReadCarrierIdTimeout）就当读码失败。</summary>
    private readonly Stopwatch _autoReadWatch = new();

    /// <summary>
    /// 载具数据
    /// </summary>
    public CarrierInfo? Info => _info;

    /// <summary>
    /// 载具这边认可了，能取放片
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
    /// ILoadPort  ICarrierIdReader 以及eap口子都拿过来
    /// </summary>
    /// <param name="port"></param>
    /// <param name="reader"></param>
    /// <param name="enqueue"></param>
    public void Attach(ILoadPort port, ICarrierIdReader? reader, Action<Action<IE87Callback>> enqueue)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(enqueue);
        _port = port;
        _reader = reader;
        _enqueue = enqueue;
    }

    /// <summary>
    /// 每拍把状态查询的两个传感器位喂进来
    /// </summary>
    /// <param name="isPresent"></param>
    /// <param name="isPlaced"></param>
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
    /// Load 带回了 Mapping 结果：更新槽图
    /// </summary>
    /// <param name="slotMap"></param>
    public void UpdateSlotMap(IReadOnlyList<SlotState> slotMap)
    {
        ArgumentNullException.ThrowIfNull(slotMap);
        if (slotMap.Count == 0)
        {
            return;
        }

        var snapshot = Array.AsReadOnly(slotMap.ToArray());
        SlotMap = snapshot;
        UpdateInfo(info => info.SlotMapStatus == CarrierSlotMapStatus.NotRead
            ? info with { SlotMapStatus = CarrierSlotMapStatus.Read }
            : info);
        ApplySlotMapToLedger(snapshot);
        NotifyE87((callback, port) => callback.SlotMapRead(port, snapshot));
    }

    /// <summary>
    /// Load 好了：这个载具进 E87 的 IN ACCESS
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
    /// Unload 好了，取放结束
    /// </summary>
    public void EndAccess()
    {
        MarkStopped();
        NotifyE87((callback, port) => callback.AccessStopped(port));
    }

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
        lock (_gate)
        {
            CarrierId = carrierId;
            // Host 改写即认定：载具号和快照一起更新，与按载具号完成的检查共用一把锁。
            UpdateInfo(info => info with { CarrierId = carrierId, IdStatus = CarrierIdStatus.Verified });
        }
        _ledger?.SetCarrierIdOn(Port.Name, carrierId);
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

    public HandleResult NoteComplete(string carrierId)
    {
        if (string.IsNullOrWhiteSpace(carrierId))
        {
            return HandleResult.Fail(ErrorCodes.CarrierIdRequired, Port.Name);
        }

        string expected = carrierId.Trim();
        lock (_gate)
        {
            var info = _info;
            if (!IsArrived || info is null)
            {
                return HandleResult.Fail(ErrorCodes.CarrierNotArrived, Port.Name);
            }

            if (string.IsNullOrWhiteSpace(info.CarrierId))
            {
                return HandleResult.Fail(ErrorCodes.CarrierIdRequired, Port.Name);
            }

            if (!string.Equals(info.CarrierId, expected, StringComparison.OrdinalIgnoreCase))
            {
                return HandleResult.Fail(ErrorCodes.CarrierIdMismatch, Port.Name, expected, info.CarrierId);
            }

            if (info.AccessStatus == CarrierAccessStatus.Complete)
            {
                return HandleResult.Success();
            }

            _info = info with { AccessStatus = CarrierAccessStatus.Complete, UpdatedAt = DateTime.Now };
        }

        NotifyE87((callback, port) => callback.CarrierComplete(port));
        return HandleResult.Success();
    }

    #endregion

    /// <summary>
    /// 组件初始化
    /// </summary>
    public override bool InitComponent()
    {
        _ledger = IocHelper.GetService<ILedgerManagerComponent>();
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
        lock (_gate)
        {
            CarrierId = carrierId;
            // 读到 ≠ 认定：接了 EAP 的话还要 Host 点头（ProceedWithCarrier）才转 Verified。
            UpdateInfo(info => info with { CarrierId = carrierId, IdStatus = CarrierIdStatus.Read });
        }
        _ledger?.SetCarrierIdOn(Port.Name, carrierId);
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
    /// 自动读码没发起成功的接着试
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
    /// 载具拿走了
    /// </summary>
    private void Remove()
    {
        string? carrierId = CarrierId;
        SlotMap = Array.Empty<SlotState>();
        _autoReadPending = false;
        lock (_gate)
        {
            CarrierId = null;
            _info = null;
        }

        _ledger?.Clear(Port.Name);
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
    /// 改载具快照
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
    /// 把 Mapping 结果落到晶圆账
    /// </summary>
    /// <param name="slotMap"></param>
    private void ApplySlotMapToLedger(IReadOnlyList<SlotState> slotMap)
    {
        if (_ledger is null)
        {
            return;
        }

        var port = Port;
        _ledger.RegisterLocation(port.Name, port.SlotCount);

        var statuses = new WaferStatus?[slotMap.Count];
        for (int index = 0; index < slotMap.Count; index++)
        {
            statuses[index] = ToWaferStatus(slotMap[index]);
        }

        var info = _info;
        int created = _ledger.ApplySlotMap(port.Name, statuses, info?.CarrierId, info?.LotId);
        LogHelper.Info($"[{port.Name}] Mapping 落账：{created} 片");
    }

    /// <summary>
    /// 槽位状态换成账上的片状态
    /// </summary>
    /// <param name="state"></param>
    /// <returns></returns>
    private static WaferStatus? ToWaferStatus(SlotState state)
    {
        switch (state)
        {
            case SlotState.Empty:
                return null;

            case SlotState.Normal:
            case SlotState.NotEmpty:
                return WaferStatus.Normal;

            case SlotState.Double:
                return WaferStatus.Double;

            case SlotState.Crossed:
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
