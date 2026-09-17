using System.Threading.Channels;
using SqlSugar;
using xyz.Common.Log;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Wafers;
using xyz.Configs.Models;
using xyz.Database.DbProvider;
using xyz.Database.Wafers;

namespace xyz.Components.Components;

/// <summary>
/// 晶圆账：全系统唯一一本"哪个位置上有哪片"的账。位置用 (模块名, 槽位) 表达，机械手的手臂也算槽位。
/// </summary>
[Component(description: "晶圆账：记录每个位置上的片与流转")]
public class WaferManager : ComponentBase
{
    /// <summary>
    /// 当前账本；sc.xml 里装出来即生效。冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static WaferManager? Current { get; set; }

    private readonly object _gate = new();

    /// <summary>模块名 → 槽位数组，下标 0 即第 1 槽；null 表示空槽。容量在注册时定死，不随运行增删。</summary>
    private readonly Dictionary<string, WaferInfo?[]> _locations = new(StringComparer.OrdinalIgnoreCase);

    public WaferManager()
    {
        Current = this;
    }

    #region SC 

    [SCEditor("True", "WaferManager", "是否记晶圆账（False=不记账，设备照常动作）")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("True", "WaferManager", "是否把每次变动写成流水入库（False=只在内存记账）")]
    public bool EnableHistory { get; set; } = true;

    [SCEditor("Default", "WaferManager", "流水落哪个库（sc.xml 的 Database 节点名），晶圆信息属业务数据，走默认库")]
    public string HistoryDatabase { get; set; } = XyzDb.DefaultName;

    [SCEditor("90", "WaferManager", "晶圆流水保留天数，超期每天清理一次")]
    public int HistoryKeepDays { get; set; } = 90;

    #endregion

    #region 流水入库（装配时按 SC 开；直接 new 出来的账本不碰数据库）

    // ⚠ 这一段里凡是连数据库的（PrepareTodayTable / WriteHistoryLoopAsync / CleanupHistory）
    //   一律不许在 lock (_gate) 里面调：磁盘一卡，所有设备线程就全堵在账本锁上。
    //   写账的地方都是出了锁、拿到 snapshot 之后再 RecordHistory，只入队不写库。

    /// <summary>一次最多攒这么多行再写，避免一行一次 IO。</summary>
    private const int HistoryBatchSize = 64;

    /// <summary>积压到这个条数的整数倍时记一次告警。</summary>
    private const int HistoryBacklogWarning = 1000;

    /// <summary>清理间隔：一天跑一次。</summary>
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    /// <summary>开机第一次清理延后一分钟：让 sc.xml 整棵树装完，别在装配途中连库。</summary>
    private static readonly TimeSpan StartupCleanupDelay = TimeSpan.FromMinutes(1);

    /// <summary>null = 不落库（没装配，或 SC 里关了）。</summary>
    private Channel<WaferHistoryEntity>? _historyRows;

    private Timer? _cleanupTimer;
    private int _historyPending;
    private DateTime _historyPreparedDay = DateTime.MinValue;

    /// <summary>
    /// 装配读完 SC 后开流水队列与写库线程；EnableHistory=False 就不开，整个账本不碰数据库。
    /// 这里不建表：sc.xml 里 Database 节点万一排在账本后面，这会儿连接还没注册，
    /// 建表就建到默认库去了。等真有流水要写（那时整棵树早装完了）再建。
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        if (!IsEnable || !EnableHistory)
        {
            return;
        }

        var rows = Channel.CreateUnbounded<WaferHistoryEntity>(new UnboundedChannelOptions { SingleReader = true });
        _historyRows = rows;
        _ = Task.Run(() => WriteHistoryLoopAsync(rows));
        _cleanupTimer = new Timer(_ => CleanupHistory(), null, StartupCleanupDelay, CleanupInterval);
    }

    /// <summary>
    /// 停止流水落库（宿主退出时调）：队列关闭、写完剩下的行后线程结束。
    /// </summary>
    public void StopHistory()
    {
        _historyRows?.Writer.TryComplete();
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;
    }

    /// <summary>
    /// 记一条流水：只入队，不写库，所以写账线程不会被数据库拖住。必须在 _gate 之外调。
    /// </summary>
    private void RecordHistory(WaferHistoryAction action, WaferInfo wafer, string? fromModule = null, int? fromSlot = null)
    {
        if (_historyRows is not { } rows)
        {
            return;
        }

        var row = new WaferHistoryEntity
        {
            Action = action.ToString(),
            WaferGuid = wafer.Id.ToString(),
            WaferId = wafer.WaferId,
            Module = wafer.Module,
            Slot = wafer.Slot,
            FromModule = fromModule,
            FromSlot = fromSlot,
            CarrierId = wafer.CarrierId,
            LotId = wafer.LotId,
            Status = wafer.Status.ToString(),
            ProcessState = wafer.ProcessState.ToString(),
            OccurredAt = wafer.UpdatedAt,
        };

        // 队列已关（StopHistory 之后）就当没记，不报错。
        if (!rows.Writer.TryWrite(row))
        {
            return;
        }

        int pending = Interlocked.Increment(ref _historyPending);
        if (pending > 0 && pending % HistoryBacklogWarning == 0)
        {
            LogHelper.Warn(Name, $"晶圆流水积压 {pending} 行，检查数据库是否卡住");
        }
    }

    /// <summary>
    /// 写库后台线程：攒一批写一次。库挂了只记日志丢这批，账照记、设备照跑。
    /// </summary>
    private async Task WriteHistoryLoopAsync(Channel<WaferHistoryEntity> rows)
    {
        var batch = new List<WaferHistoryEntity>(HistoryBatchSize);
        while (await rows.Reader.WaitToReadAsync())
        {
            batch.Clear();
            while (batch.Count < HistoryBatchSize && rows.Reader.TryRead(out var row))
            {
                Interlocked.Decrement(ref _historyPending);
                batch.Add(row);
            }

            if (batch.Count == 0)
            {
                continue;
            }

            try
            {
                PrepareTodayTable();
                using var db = XyzDb.Create(HistoryDatabase);
                db.Insertable(batch).SplitTable().ExecuteCommand();
            }
            catch (Exception exception)
            {
                // 写库失败不能拖住设备：丢这一批并记日志，账本本身不受影响。
                LogHelper.Warn(Name, $"晶圆流水写库失败，丢弃 {batch.Count} 行: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// 建当天的分表；跨天后写下一批之前再建一次。
    /// </summary>
    private void PrepareTodayTable()
    {
        if (_historyPreparedDay == DateTime.Today)
        {
            return;
        }

        if (!XyzDb.IsRegistered(HistoryDatabase))
        {
            LogHelper.Warn(Name, $"晶圆流水库 {HistoryDatabase} 没在 sc.xml 的 Database 节点里配，暂时落到默认库");
        }

        using var db = XyzDb.Create(HistoryDatabase);
        db.CodeFirst.SplitTables().InitTables<WaferHistoryEntity>();
        _historyPreparedDay = DateTime.Today;
    }

    /// <summary>
    /// 清理过期流水：按天分表，直接删整张过期的日表，不用逐行删；失败只记日志，下次再试。
    /// </summary>
    private void CleanupHistory()
    {
        try
        {
            var deadline = DateTime.Today.AddDays(-HistoryKeepDays);
            using var db = XyzDb.Create(HistoryDatabase);
            var expired = db.SplitHelper<WaferHistoryEntity>().GetTables()
                .Where(table => table.Date < deadline)
                .ToList();

            foreach (var table in expired)
            {
                db.DbMaintenance.DropTable(table.TableName);
            }

            if (expired.Count > 0)
            {
                LogHelper.Info($"[{Name}] 清理晶圆流水 {expired.Count} 张过期日表（保留 {HistoryKeepDays} 天）");
            }
        }
        catch (Exception exception)
        {
            LogHelper.Warn(Name, $"清理晶圆流水失败: {exception.Message}");
        }
    }

    #endregion

    #region Alarm

    [Alarm("晶圆账异常", AlarmCategory.ProcessError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "账本与实际不符：源位置无片、目标位置已有片，或传感器与账对不上",
        Solution = "在手动界面按实际情况建片/移片/删片，核对后再继续")]
    public string WaferLedgerAlarm = nameof(WaferLedgerAlarm);

    #endregion

    #region 事件 

    /// <summary>建片。</summary>
    public event Action<WaferInfo>? WaferCreated;

    /// <summary>删片。</summary>
    public event Action<WaferInfo>? WaferDeleted;

    /// <summary>移片，参数为片（已在新位置）、原模块、原槽位。</summary>
    public event Action<WaferInfo, string, int>? WaferMoved;

    /// <summary>片的信息变了（片号、批次、载具、工艺状态）。</summary>
    public event Action<WaferInfo>? WaferUpdated;

    #endregion

    #region 注册槽位

    /// <summary>
    /// 注册一个位置有多少槽（LoadPort 按花篮槽数、机械手按手指数、腔体 1 个）。
    /// 装配或 Open 时调一次；重复注册同样的槽数是空操作，槽数变了会重建该模块的账。
    /// </summary>
    public void RegisterLocation(string module, int slotCount)
    { 
        lock (_gate)
        {
            if (_locations.TryGetValue(module, out var existing) && existing.Length == slotCount)
            {
                return;
            }

            if (existing is not null)
            {
                LogHelper.Warn(Name, $"{module} 槽数由 {existing.Length} 改为 {slotCount}，该模块的账已重建");
            }

            _locations[module] = new WaferInfo?[slotCount];
        }
    }

    /// <summary>
    /// 这个位置有没有注册过。
    /// </summary>
    public bool IsRegistered(string module)
    {
        lock (_gate)
        {
            return _locations.ContainsKey(module);
        }
    }

    #endregion

    #region 写账（建 / 删 / 移 / 改）

    /// <summary>
    /// 建片；位置已有片或没注册时不建，记错误日志并返回 null。
    /// </summary>
    public WaferInfo? Create(string module, int slot, WaferStatus status = WaferStatus.Normal,
        string? carrierId = null, string? lotId = null)
    {
        if (!IsEnable)
        {
            return null;
        }

        WaferInfo created;
        lock (_gate)
        {
            if (!TryGetSlots(module, slot, out var slots))
            {
                return null;
            }

            if (slots[slot - 1] is { } occupied)
            {
                Fault($"{module}.{slot:00} 已经有片 {occupied.WaferId}，不能重复建片");
                return null;
            }

            created = new WaferInfo(module, slot, status, carrierId, lotId);
            slots[slot - 1] = created;
        }

        var snapshot = created.Clone();
        WaferCreated?.Invoke(snapshot);
        RecordHistory(WaferHistoryAction.Created, snapshot);
        return snapshot;
    }

    /// <summary>
    /// 删片；位置没片时记错误日志并返回 false。
    /// </summary>
    public bool Delete(string module, int slot)
    {
        if (!IsEnable)
        {
            return false;
        }

        WaferInfo deleted;
        lock (_gate)
        {
            if (!TryGetSlots(module, slot, out var slots))
            {
                return false;
            }

            if (slots[slot - 1] is not { } wafer)
            {
                Fault($"{module}.{slot:00} 上没片，删不了");
                return false;
            }

            deleted = wafer;
            slots[slot - 1] = null;
        }

        var snapshot = deleted.Clone();
        WaferDeleted?.Invoke(snapshot);
        RecordHistory(WaferHistoryAction.Deleted, snapshot);
        return true;
    }

    /// <summary>
    /// 清掉一个模块上的全部片（载具移走、花篮整篮换掉时用），返回删了几片。
    /// </summary>
    public int Clear(string module)
    {
        if (!IsEnable)
        {
            return 0;
        }

        var deleted = new List<WaferInfo>();
        lock (_gate)
        {
            if (!_locations.TryGetValue(module, out var slots))
            {
                return 0;
            }

            for (int index = 0; index < slots.Length; index++)
            {
                if (slots[index] is { } wafer)
                {
                    deleted.Add(wafer);
                    slots[index] = null;
                }
            }
        }

        foreach (var wafer in deleted)
        {
            var snapshot = wafer.Clone();
            WaferDeleted?.Invoke(snapshot);
            RecordHistory(WaferHistoryAction.Deleted, snapshot);
        }

        return deleted.Count;
    }

    /// <summary>
    /// 移片：校验与搬运在同一把锁里做完，不会出现"两边都以为自己能放"的竞态。
    /// 源无片或目标已有片都算账实不符：报警并返回 false，调用方应停下来让人对账。
    /// </summary>
    public bool Move(string fromModule, int fromSlot, string toModule, int toSlot)
    {
        if (!IsEnable)
        {
            return false;
        }

        WaferInfo moved;
        lock (_gate)
        {
            if (!TryGetSlots(fromModule, fromSlot, out var source) || !TryGetSlots(toModule, toSlot, out var target))
            {
                return false;
            }

            if (source[fromSlot - 1] is not { } wafer)
            {
                Fault($"{fromModule}.{fromSlot:00} 上没片，移不了");
                return false;
            }

            if (ReferenceEquals(source, target) && fromSlot == toSlot)
            {
                return true;
            }

            if (target[toSlot - 1] is { } blocked)
            {
                Fault($"{toModule}.{toSlot:00} 上已经有片 {blocked.WaferId}，{wafer.WaferId} 移不过去");
                return false;
            }

            source[fromSlot - 1] = null;
            target[toSlot - 1] = wafer;
            wafer.Module = toModule;
            wafer.Slot = toSlot;
            wafer.UpdatedAt = DateTime.Now;
            moved = wafer;
        }

        var snapshot = moved.Clone();
        WaferMoved?.Invoke(snapshot, fromModule, fromSlot);
        RecordHistory(WaferHistoryAction.Moved, snapshot, fromModule, fromSlot);
        return true;
    }

    /// <summary>
    /// 按 Mapping 结果整篮重建：先清掉这个模块的旧账，再按每槽状态建片（null 表示空槽）。
    /// slots 下标 0 即第 1 槽；返回建了几片。
    /// </summary>
    public int ApplySlotMap(string module, IReadOnlyList<WaferStatus?> slots, string? carrierId = null, string? lotId = null)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (!IsEnable)
        {
            return 0;
        }

        Clear(module);

        int created = 0;
        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index] is { } status && Create(module, index + 1, status, carrierId, lotId) is not null)
            {
                created++;
            }
        }

        return created;
    }

    /// <summary>改片号（读码成功或 Host 改写）。</summary>
    public bool SetWaferId(string module, int slot, string waferId)
    {
        return Update(module, slot, wafer => wafer.WaferId = waferId);
    }

    /// <summary>改批次号。</summary>
    public bool SetLotId(string module, int slot, string? lotId)
    {
        return Update(module, slot, wafer => wafer.LotId = lotId);
    }

    /// <summary>改所属载具。</summary>
    public bool SetCarrierId(string module, int slot, string? carrierId)
    {
        return Update(module, slot, wafer => wafer.CarrierId = carrierId);
    }

    /// <summary>
    /// 把一个模块上所有片的载具号一起改掉，返回改了几片。
    /// 用在读码与 Mapping 先后顺序不定的场合：Mapping 先到就先建片（载具号还是空的），
    /// 读码回来之后用这个补上。
    /// </summary>
    public int SetCarrierIdOn(string module, string? carrierId)
    {
        if (!IsEnable)
        {
            return 0;
        }

        int count = 0;
        var slots = GetSlots(module);
        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index] is not null && SetCarrierId(module, index + 1, carrierId))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>改工艺状态。</summary>
    public bool SetProcessState(string module, int slot, WaferProcessState state)
    {
        return Update(module, slot, wafer => wafer.ProcessState = state);
    }

    private bool Update(string module, int slot, Action<WaferInfo> change)
    {
        if (!IsEnable)
        {
            return false;
        }

        WaferInfo updated;
        lock (_gate)
        {
            if (!TryGetSlots(module, slot, out var slots))
            {
                return false;
            }

            if (slots[slot - 1] is not { } wafer)
            {
                Fault($"{module}.{slot:00} 上没片，改不了");
                return false;
            }

            change(wafer);
            wafer.UpdatedAt = DateTime.Now;
            updated = wafer;
        }

        var snapshot = updated.Clone();
        WaferUpdated?.Invoke(snapshot);
        RecordHistory(WaferHistoryAction.Updated, snapshot);
        return true;
    }

    #endregion

    #region 查账

    /// <summary>查一个位置上的片；空槽或没注册返回 null。</summary>
    public WaferInfo? Get(string module, int slot)
    {
        lock (_gate)
        {
            if (!_locations.TryGetValue(module, out var slots) || slot < 1 || slot > slots.Length)
            {
                return null;
            }

            return slots[slot - 1]?.Clone();
        }
    }

    /// <summary>查一个模块的全部槽位，下标 0 即第 1 槽，空槽为 null；没注册返回空表。</summary>
    public IReadOnlyList<WaferInfo?> GetSlots(string module)
    {
        lock (_gate)
        {
            if (!_locations.TryGetValue(module, out var slots))
            {
                return Array.Empty<WaferInfo?>();
            }

            var snapshot = new WaferInfo?[slots.Length];
            for (int index = 0; index < slots.Length; index++)
            {
                snapshot[index] = slots[index]?.Clone();
            }

            return snapshot;
        }
    }

    /// <summary>这个位置有没有片。</summary>
    public bool HasWafer(string module, int slot)
    {
        lock (_gate)
        {
            return _locations.TryGetValue(module, out var slots)
                   && slot >= 1 && slot <= slots.Length
                   && slots[slot - 1] is not null;
        }
    }

    /// <summary>这个位置是不是空的（没注册也算不可用，返回 false）。</summary>
    public bool IsEmpty(string module, int slot)
    {
        lock (_gate)
        {
            return _locations.TryGetValue(module, out var slots)
                   && slot >= 1 && slot <= slots.Length
                   && slots[slot - 1] is null;
        }
    }

    /// <summary>这个模块上有几片。</summary>
    public int CountWafers(string module)
    {
        lock (_gate)
        {
            if (!_locations.TryGetValue(module, out var slots))
            {
                return 0;
            }

            return slots.Count(wafer => wafer is not null);
        }
    }

    /// <summary>按片号找片；找不到返回 null（同号取第一片）。</summary>
    public WaferInfo? Find(string waferId)
    {
        if (string.IsNullOrWhiteSpace(waferId))
        {
            return null;
        }

        lock (_gate)
        {
            foreach (var slots in _locations.Values)
            {
                foreach (var wafer in slots)
                {
                    if (wafer is not null && string.Equals(wafer.WaferId, waferId, StringComparison.OrdinalIgnoreCase))
                    {
                        return wafer.Clone();
                    }
                }
            }

            return null;
        }
    }

    #endregion

    #region 对账

    /// <summary>
    /// 拿设备实际在位跟账核对（机械手手指在位、花篮槽位传感器）：一致返回 true；
    /// 不一致报警并返回 false，由调用方决定是停下来还是按实际补账。
    /// </summary>
    public bool Verify(string module, int slot, bool deviceHasWafer)
    {
        bool onLedger = HasWafer(module, slot);
        if (onLedger == deviceHasWafer)
        {
            return true;
        }

        Fault(onLedger
            ? $"{module}.{slot:00} 账上有片，设备上没有"
            : $"{module}.{slot:00} 设备上有片，账上没有");
        return false;
    }

    #endregion

    /// <summary>
    /// 槽位定位：模块没注册或槽位越界都算调用方写错了，记错误日志（不抛异常，避免打断设备流程）。
    /// </summary>
    private bool TryGetSlots(string module, int slot, out WaferInfo?[] slots)
    {
        if (!_locations.TryGetValue(module, out slots!))
        {
            Fault($"{module} 没注册过槽位");
            return false;
        }

        if (slot < 1 || slot > slots.Length)
        {
            Fault($"{module} 没有 {slot} 号槽位（共 {slots.Length} 槽）");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 账实不符：记日志并报警。不能只写日志——CTC 那边就是因为静默，现场表现成"片凭空消失"。
    /// </summary>
    private void Fault(string message)
    {
        LogHelper.Error(Name, $"晶圆账: {message}");
        AlarmComponent.Current?.Raise(this, WaferLedgerAlarm);
    }
}
