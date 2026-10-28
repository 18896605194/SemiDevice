using System.Threading.Channels;
using SqlSugar;
using xyz.Common.Events;
using xyz.Common.Log;
using xyz.Elements.Attributes;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Configs.Models;
using xyz.Database.DbProvider;
using xyz.Database.Wafers;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Elements.Elements;

/// <summary>
/// 晶圆账 应用到各个模块
/// </summary>
[Element(description: "晶圆账：记录每个位置上的片与流转")]
public class LedgerManagerElement : ElementBase, ILedgerManager
{
    private readonly object _gate = new();

    /// <summary>
    /// 模块名 → 槽位数组
    /// </summary>
    private readonly Dictionary<string, WaferInfo?[]> _locations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按 LoadPort 注册的位置
    /// </summary>
    private readonly HashSet<string> _loadPorts = new(StringComparer.OrdinalIgnoreCase);

    public LedgerManagerElement()
    {

        // 账一变就给 EAP 报一份（E90）：放进 EAP 的派发元件按先后报，不占改账的线程。没挂 EAP 时不入队
        WaferCreated += wafer => NotifyE90(callback => callback.WaferCreated(wafer));
        WaferDeleted += wafer => NotifyE90(callback => callback.WaferDeleted(wafer));
        WaferMoved += (wafer, fromModule, fromSlot) => NotifyE90(callback => callback.WaferMoved(wafer, fromModule, fromSlot));
        WaferUpdated += wafer => NotifyE90(callback => callback.WaferUpdated(wafer));

        // 账一变就告诉界面哪个站点变了（只是轻通知：整篮 Mapping 变化很密，界面自己攒一下再拉）
        WaferCreated += wafer => NotifyLedger(wafer.Module);
        WaferDeleted += wafer => NotifyLedger(wafer.Module);
        WaferUpdated += wafer => NotifyLedger(wafer.Module);
        WaferMoved += (wafer, _, _) => NotifyLedger(wafer.Module);
    }

    /// <summary>
    /// 映射元件（开机准备时取）：存盘行、流水行、人工调整记录都靠它转
    /// </summary>
    private IMapper? _mapper;

    /// <summary>
    /// EAP 的派发元件（开机准备时取）；没挂 EAP 是 null
    /// </summary>
    private IEapNotifierElement? _notifier;

    /// <summary>
    /// 开机准备：取映射元件、EAP 派发元件。排在各模块前面，模块开机准备时就登记槽位、可能建片记流水
    /// </summary>
    public override bool InitElement()
    {
        _mapper = IocHelper.GetService<IMapper>();
        _notifier = IocHelper.GetService<IEapNotifierElement>();
        return base.InitElement();
    }

    /// <summary>
    /// 开机准备都做完（各模块登记完槽位）、开始扫描之前：按存盘把账恢复回来
    /// </summary>
    public override void Bind()
    {
        Restore();
    }

    private static void NotifyLedger(string module)
    {
        RemoteEventPublisher.Send(new WaferLedgerChangedDto { Module = module }, WaferLedgerDto.EventToken, retain: false);
    }

    #region EAP 口子（E90 片跟踪）

    public IE90Callback? E90Callback { get; set; }

    private void NotifyE90(Action<IE90Callback> notification)
    {
        if (E90Callback is null)
        {
            return;
        }

        _notifier?.Post(() =>
        {
            var callback = E90Callback;
            if (callback is not null)
            {
                notification(callback);
            }
        });
    }

    public IReadOnlyList<(string Module, int SlotCount)> Locations
    {
        get
        {
            lock (_gate)
            {
                return _locations.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => (pair.Key, pair.Value.Length)).ToList();
            }
        }
    }

    public bool IsLoadPort(string module)
    {
        lock (_gate)
        {
            return _loadPorts.Contains(module);
        }
    }

    #endregion

    #region Machine 

    [Machine("True", "LedgerManager", "是否记晶圆账（False=不记账，设备照常动作）")]
    public bool IsEnable { get; set; } = true;

    [Machine("True", "LedgerManager", "是否把每次变动写成流水入库（False=只在内存记账）")]
    public bool EnableHistory { get; set; } = true;

    [Machine("True", "LedgerManager", "是否把账存盘、开机恢复腔体和机械手上的片（LoadPort 以开机 Mapping 为准；False=只在内存，重启后机内的片要人工补账）")]
    public bool IsPersistent { get; set; } = true;

    [Machine("Default", "LedgerManager", "流水和存盘落哪个库（machine.xml 的 Database 节点名），晶圆信息属业务数据，走默认库")]
    public string HistoryDatabase { get; set; } = XyzDb.DefaultName;

    [Machine("90", "LedgerManager", "晶圆流水保留天数，超期每天清理一次")]
    public int HistoryKeepDays { get; set; } = 90;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "条", "1", "1000", "64", "晶圆流水入库批量：写库线程每次最多攒这么多条写一次")]
    public int HistoryBatchSize
    {
        get { return GetEcInt(nameof(HistoryBatchSize)); }
        set { SetEcInt(nameof(HistoryBatchSize), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "条", "100", "100000", "1000", "入库积压告警：待写库的流水攒到这个条数的整数倍时记一次告警日志")]
    public int HistoryBacklogWarning
    {
        get { return GetEcInt(nameof(HistoryBacklogWarning)); }
        set { SetEcInt(nameof(HistoryBacklogWarning), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "10000", "500", "账存盘间隔：账变了以后最多隔这么久把当前账整张写一次库")]
    public int SnapshotIntervalMs
    {
        get { return GetEcInt(nameof(SnapshotIntervalMs)); }
        set { SetEcInt(nameof(SnapshotIntervalMs), value); }
    }

    #endregion

    #region 入库
    
    private Channel<WaferHistoryEntity>? _historyRows;

    private int _historyPending;

    /// <summary>
    /// 装配
    /// </summary>
    /// <param name="setting"></param>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        if (!IsEnable)
        {
            return;
        }

        if (EnableHistory)
        {
            var rows = Channel.CreateUnbounded<WaferHistoryEntity>(new UnboundedChannelOptions { SingleReader = true });
            _historyRows = rows;
            _ = Task.Run(() => WriteHistoryLoopAsync(rows));
        }

        if (IsPersistent)
        {
            _persisting = true;
            _ = Task.Run(SnapshotLoopAsync);
        }
    }

    /// <summary>
    /// 停止写到数据库
    /// </summary>
    public void StopHistory()
    {
        _historyRows?.Writer.TryComplete();
    }

    /// <summary>
    /// 记一条流水：只入队，不写库
    /// </summary>
    private void RecordHistory(WaferHistoryAction action, WaferInfo wafer, string? fromModule = null, int? fromSlot = null)
    {
        Interlocked.Exchange(ref _snapshotDirty, 1);

        // 映射元件开机准备时取、开机恢复时查过在；没走开机准备的（冒烟直接 new 的）才会是 null
        var rows = _historyRows;
        if (rows is null || _mapper is null)
        {
            return;
        }

        var row = _mapper.Map<WaferHistoryEntity>(wafer);
        row.Action = action.ToString();
        row.FromModule = fromModule;
        row.FromSlot = fromSlot;

        // 队列已关（StopHistory 之后）就当没记，不报错。
        if (!rows.Writer.TryWrite(row))
        {
            return;
        }

        int pending = Interlocked.Increment(ref _historyPending);
        int backlogWarning = HistoryBacklogWarning;
        if (backlogWarning > 0 && pending > 0 && pending % backlogWarning == 0)
        {
            LogHelper.Warn(this, $"晶圆流水积压 {pending} 行，检查数据库是否卡住");
        }
    }

    /// <summary>
    /// 写库
    /// </summary>
    private async Task WriteHistoryLoopAsync(Channel<WaferHistoryEntity> rows)
    {
        var batch = new List<WaferHistoryEntity>();
        while (await rows.Reader.WaitToReadAsync())
        {
            batch.Clear();
            int batchSize = Math.Max(1, HistoryBatchSize);
            while (batch.Count < batchSize && rows.Reader.TryRead(out var row))
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
                CleanupIfDayChanged();
                using var db = XyzDb.Create(HistoryDatabase);
                db.Insertable(batch).SplitTable().ExecuteCommand(); //插入数据库
            }
            catch (Exception exception)
            {
                LogHelper.Warn(this, $"晶圆流水写库失败，丢弃 {batch.Count} 行: {exception.Message}");
            }
        }
    }

    #endregion

    #region 存盘与开机恢复（当前账整张存一份；开机放回腔体、机械手上的片，LoadPort 以开机 Mapping 为准）

    /// <summary>
    /// 持久化
    /// </summary>
    private bool _persisting;

    /// <summary>
    /// 开机恢复做完了才开始存盘
    /// </summary>
    private volatile bool _restored;

    /// <summary>
    /// 账变过、还没存盘为 1
    /// </summary>
    private int _snapshotDirty;

    /// <summary>
    /// 上一次存盘没成：再失败不重复记日志，存成了记一条恢复正常
    /// </summary>
    private bool _snapshotFailing;

    private bool _currentTableReady;

    /// <summary>
    /// 开机恢复  从WaferCurrentEntity  实时最新的表恢复，这个表是和内存同步的
    /// </summary>
    /// <returns></returns>
    public int Restore()
    {
        // 存盘行、流水行、人工调整记录都靠映射元件转，账本开着却没配它：开机就报出来
        if (IsEnable && _mapper is null)
        {
            throw new InvalidOperationException($"{Name} 的存盘、流水、人工调整记录要靠映射元件转，但 machine.xml 里没配映射元件（MapperElement）");
        }

        if (!IsEnable || !_persisting)
        {
            _restored = true;
            return 0;
        }

        List<WaferCurrentEntity> rows;
        try
        {
            using var db = XyzDb.Create(HistoryDatabase);
            EnsureCurrentTable(db);
            rows = db.Queryable<WaferCurrentEntity>().ToList();
        }
        catch (Exception exception)
        {
            // 读不出来就从空账开始、存盘照开：这一轮的账才是真的
            _restored = true;
            LogHelper.Error(this, $"晶圆账开机恢复读库失败，账从空的开始（机内的片要人工补账）：{exception.Message}");
            return 0;
        }

        var restored = new List<WaferInfo>();
        var interrupted = new List<string>();
        var dropped = new List<string>();
        int onLoadPorts = 0;
        lock (_gate)
        {
            foreach (var row in rows)
            {
                if (_loadPorts.Contains(row.Module)) //loadport跳过
                {
                    onLoadPorts++;
                    continue;
                }

                var wafer = FromEntity(row);
                if (wafer is null || !_locations.TryGetValue(row.Module, out var slots)
                    || row.Slot < 1 || row.Slot > slots.Length || slots[row.Slot - 1] is not null)
                {
                    dropped.Add($"{row.WaferId}@{row.Module}.{row.Slot:00}");
                    continue;
                }

                slots[row.Slot - 1] = wafer;
                restored.Add(wafer.Clone());
                if (string.Equals(row.ProcessState, nameof(WaferProcessState.InProcess), StringComparison.OrdinalIgnoreCase))
                {
                    interrupted.Add(wafer.WaferId);
                }
            }
        }

        _restored = true; //已经恢复了
        Interlocked.Exchange(ref _snapshotDirty, 1);
        foreach (var wafer in restored)
        {
            RecordHistory(WaferHistoryAction.Restored, wafer);
        }

        if (restored.Count > 0)
        {
            string list = string.Join("、", restored.Select(wafer => $"{wafer.WaferId}（{wafer.Module}.{wafer.Slot:00}）"));
            string cut = interrupted.Count > 0 ? $"；{string.Join("、", interrupted)} 重启前在加工，记成中止" : string.Empty;
            LogHelper.Warn(this, $"开机恢复晶圆账：机内还有 {restored.Count} 片 {list}{cut}；到现场确认片位后用全部回片送回来源槽");
        }
        else
        {
            LogHelper.Info(this, "开机恢复晶圆账：机内没有片");
        }

        if (onLoadPorts > 0)
        {
            LogHelper.Info(this, $"LoadPort 上的 {onLoadPorts} 片不恢复，以开机 Mapping 为准");
        }

        if (dropped.Count > 0)
        {
            LogHelper.Warn(this, $"存盘里有 {dropped.Count} 片放不回去（位置没装、槽号越界、槽上已经有片或数据认不出来），已丢掉：{string.Join("、", dropped)}");
        }

        return restored.Count;
    }

    /// <summary>存盘停了（宿主退出时）：存盘线程不再写。</summary>
    private volatile bool _snapshotStopped;

    /// <summary>
    /// 停止存盘
    /// </summary>
    public void StopSnapshot()
    {
        if (!_persisting || _snapshotStopped)
        {
            return;
        }

        _snapshotStopped = true;
        if (_restored)
        {
            SaveSnapshot();
        }
    }

    /// <summary>
    /// 存盘线程：每隔 SnapshotIntervalMs 看一眼
    /// </summary>
    /// <returns></returns>
    private async Task SnapshotLoopAsync()
    {
        while (!_snapshotStopped)
        {
            await Task.Delay(Math.Max(100, SnapshotIntervalMs)).ConfigureAwait(false);
            if (_snapshotStopped || !_restored || Interlocked.Exchange(ref _snapshotDirty, 0) == 0)
            {
                continue;
            }

            if (!SaveSnapshot())
            {
                Interlocked.Exchange(ref _snapshotDirty, 1);
            }
        }
    }

    /// <summary>
    /// 把当前账整张写一次库
    /// </summary>
    public bool SaveSnapshot()
    {
        // 映射元件开机准备时取、开机恢复时查过在；没走开机准备的（冒烟直接 new 的）才会是 null
        if (_mapper is null)
        {
            return false;
        }

        var rows = new List<WaferCurrentEntity>();
        lock (_gate)
        {
            foreach (var slots in _locations.Values)
            {
                foreach (var wafer in slots)
                {
                    if (wafer is not null)
                    {
                        rows.Add(_mapper.Map<WaferCurrentEntity>(wafer));
                    }
                }
            }
        }

        try
        {
            using var db = XyzDb.Create(HistoryDatabase);
            EnsureCurrentTable(db);
            string table = db.EntityMaintenance.GetTableName<WaferCurrentEntity>();
            var result = db.Ado.UseTran(() =>
            {
                db.Ado.ExecuteCommand($"DELETE FROM {table}");
                if (rows.Count > 0)
                {
                    db.Insertable(rows).ExecuteCommand();
                }
            });
            if (!result.IsSuccess)
            {
                throw result.ErrorException;
            }
        }
        catch (Exception exception)
        {
            if (!_snapshotFailing)
            {
                _snapshotFailing = true;
                LogHelper.Warn(this, $"晶圆账存盘失败（账照记，下一轮再存）：{exception.Message}");
            }

            return false;
        }

        if (_snapshotFailing)
        {
            _snapshotFailing = false;
            LogHelper.Info(this, "晶圆账存盘恢复正常");
        }

        return true;
    }

    /// <summary>当前账表第一次用到时建（已有就只补列）。</summary>
    private void EnsureCurrentTable(ISqlSugarClient db)
    {
        if (_currentTableReady)
        {
            return;
        }

        db.CodeFirst.InitTables<WaferCurrentEntity>();
        _currentTableReady = true;
    }

    /// <summary>
    /// 存盘的一行 → 账上的一片（内部标识、来处原样）；标识、状态认不出来的返回 null。
    /// 重启前在加工的记成中止；最后变动时刻记成恢复的这一刻（流水按它分表）。
    /// </summary>
    private static WaferInfo? FromEntity(WaferCurrentEntity row)
    {
        if (!Guid.TryParse(row.WaferGuid, out var id)
            || !Enum.TryParse(row.Status, ignoreCase: true, out WaferStatus status)
            || !Enum.TryParse(row.ProcessState, ignoreCase: true, out WaferProcessState processState))
        {
            return null;
        }

        if (processState == WaferProcessState.InProcess)
        {
            processState = WaferProcessState.Aborted;
        }

        var wafer = new WaferInfo(id, row.WaferId, row.Module, row.Slot, row.OriginModule, row.OriginSlot,
            row.SourceLoadPort, row.SourceSlot, row.OriginCarrierId, row.CarrierId, row.LotId,
            status, processState, row.CreatedAt, DateTime.Now);
        wafer.Path = ParsePath(row.Path);
        return wafer;
    }

    /// <summary>
    /// 走过的位置存盘时的写法：模块名逗号隔开
    /// </summary>
    internal static string PathText(IReadOnlyList<string> path)
    {
        return string.Join(",", path);
    }

    /// <summary>
    /// 存盘里走过的位置读回来；老表没这一列（空）就是没记
    /// </summary>
    private static IReadOnlyList<string> ParsePath(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    #endregion

    #region Alarm

    [Alarm("晶圆账异常", AlarmCategory.DataIntegrity,
        AlarmLevel = AlarmLevel.Light,
        Description = "账本与实际不符：源位置无片、目标位置已有片，或传感器与账对不上",
        Solution = "在手动界面按实际情况建片/移片/删片，核对后再继续")]
    public string WaferLedgerAlarm = nameof(WaferLedgerAlarm);

    #endregion

    #region 事件 

    public event Action<WaferInfo>? WaferCreated;

    public event Action<WaferInfo>? WaferDeleted;

    public event Action<WaferInfo, string, int>? WaferMoved;

    public event Action<WaferInfo>? WaferUpdated;

    #endregion

    #region 注册槽位

    /// <summary>
    /// 注册一个位置有多少槽
    /// </summary>
    /// <param name="module"></param>
    /// <param name="slotCount"></param>
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
                LogHelper.Warn(this, $"{module} 槽数由 {existing.Length} 改为 {slotCount}，该模块的账已重建");
            }

            _locations[module] = new WaferInfo?[slotCount];
        }
    }

    /// <summary>
    /// 注册一个 LoadPort 位置
    /// </summary>
    /// <param name="module"></param>
    /// <param name="slotCount"></param>
    public void RegisterLoadPort(string module, int slotCount)
    {
        RegisterLocation(module, slotCount);
        lock (_gate)
        {
            _loadPorts.Add(module);
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
    /// 建片
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

            var occupied = slots[slot - 1];
            if (occupied is not null)
            {
                Fault($"{module}.{slot:00} 已经有片 {occupied.WaferId}，不能重复建片");
                return null;
            }

            created = new WaferInfo(module, slot, status, carrierId, lotId, _loadPorts.Contains(module));
            slots[slot - 1] = created;
        }

        var snapshot = created.Clone();
        WaferCreated?.Invoke(snapshot);
        RecordHistory(WaferHistoryAction.Created, snapshot);
        return snapshot;
    }

    /// <summary>
    /// 删片
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

            var wafer = slots[slot - 1];
            if (wafer is null)
            {
                Fault($"{module}.{slot:00} 上没片，删不了");
                return false;
            }

            deleted = wafer;
            deleted.UpdatedAt = DateTime.Now;  // 流水按这个时间记、按它分表，得是删的时刻
            slots[slot - 1] = null;
        }

        var snapshot = deleted.Clone();
        WaferDeleted?.Invoke(snapshot);
        RecordHistory(WaferHistoryAction.Deleted, snapshot);
        return true;
    }

    /// <summary>
    /// 清片
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
                var wafer = slots[index];
                if (wafer is not null)
                {
                    wafer.UpdatedAt = DateTime.Now;
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
    /// 移片
    /// </summary>
    /// <param name="fromModule"></param>
    /// <param name="fromSlot"></param>
    /// <param name="toModule"></param>
    /// <param name="toSlot"></param>
    /// <returns></returns>
    public bool Move(string fromModule, int fromSlot, string toModule, int toSlot)
    {
        return MoveCore(fromModule, fromSlot, toModule, toSlot, expectedWaferId: null);
    }

    /// <summary>核对已有晶圆内部 Id 后原子移账，防止动作期间来源账被替换。</summary>
    public bool Move(string fromModule, int fromSlot, string toModule, int toSlot, Guid expectedWaferId)
    {
        return MoveCore(fromModule, fromSlot, toModule, toSlot, expectedWaferId);
    }

    /// <summary>
    /// 片移到新位置后走过的位置：回到 LoadPort 清空，别的位置接在后面
    /// </summary>
    private IReadOnlyList<string> NextPath(IReadOnlyList<string> path, string module)
    {
        if (_loadPorts.Contains(module))
        {
            return [];
        }

        return [.. path, module];
    }

    private bool MoveCore(string fromModule, int fromSlot, string toModule, int toSlot, Guid? expectedWaferId)
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

            var wafer = source[fromSlot - 1];
            if (wafer is null)
            {
                Fault($"{fromModule}.{fromSlot:00} 上没片，移不了");
                return false;
            }

            if (expectedWaferId.HasValue && wafer.Id != expectedWaferId.Value)
            {
                Fault($"{fromModule}.{fromSlot:00} 上的晶圆已变化：预期 {expectedWaferId.Value}，当前 {wafer.Id}（{wafer.WaferId}），不移账");
                return false;
            }

            if (ReferenceEquals(source, target) && fromSlot == toSlot)
            {
                return true;
            }

            var blocked = target[toSlot - 1];
            if (blocked is not null)
            {
                Fault($"{toModule}.{toSlot:00} 上已经有片 {blocked.WaferId}，{wafer.WaferId} 移不过去");
                return false;
            }

            source[fromSlot - 1] = null;
            target[toSlot - 1] = wafer;
            wafer.Module = toModule;
            wafer.Slot = toSlot;
            wafer.Path = NextPath(wafer.Path, toModule);
            wafer.UpdatedAt = DateTime.Now;
            moved = wafer;
        }

        var snapshot = moved.Clone();
        WaferMoved?.Invoke(snapshot, fromModule, fromSlot);
        RecordHistory(WaferHistoryAction.Moved, snapshot, fromModule, fromSlot);
        return true;
    }

    /// <summary>
    ///  按 Mapping 结果整篮重建
    /// </summary>
    /// <param name="module"></param>
    /// <param name="slots"></param>
    /// <param name="carrierId"></param>
    /// <param name="lotId"></param>
    /// <returns></returns>
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
            var status = slots[index];
            if (status is not null && Create(module, index + 1, status.Value, carrierId, lotId) is not null)
            {
                created++;
            }
        }

        return created;
    }

    /// <summary>
    /// 改片号
    /// </summary>
    /// <param name="module"></param>
    /// <param name="slot"></param>
    /// <param name="waferId"></param>
    /// <returns></returns>
    public bool SetWaferId(string module, int slot, string waferId)
    {
        return Update(module, slot, wafer => wafer.WaferId = waferId);
    }

    /// <summary>
    /// 改批次号
    /// </summary>
    /// <param name="module"></param>
    /// <param name="slot"></param>
    /// <param name="lotId"></param>
    /// <returns></returns>
    public bool SetLotId(string module, int slot, string? lotId)
    {
        return Update(module, slot, wafer => wafer.LotId = lotId);
    }

    /// <summary>
    /// 改所属载具
    /// </summary>
    /// <param name="module"></param>
    /// <param name="slot"></param>
    /// <param name="carrierId"></param>
    /// <returns></returns>
    public bool SetCarrierId(string module, int slot, string? carrierId)
    {
        return Update(module, slot, wafer => wafer.CarrierId = carrierId);
    }

    /// <summary>
    /// 把一个模块上所有片的载具号一起改掉，返回改了几片
    /// </summary>
    /// <param name="module"></param>
    /// <param name="carrierId"></param>
    /// <returns></returns>
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

    /// <summary>
    /// 改工艺状态。
    /// </summary>
    /// <param name="module"></param>
    /// <param name="slot"></param>
    /// <param name="state"></param>
    /// <returns></returns>
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

            var wafer = slots[slot - 1];
            if (wafer is null)
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

    public bool HasWafer(string module, int slot)
    {
        lock (_gate)
        {
            return _locations.TryGetValue(module, out var slots)
                   && slot >= 1 && slot <= slots.Length
                   && slots[slot - 1] is not null;
        }
    }

    public bool IsEmpty(string module, int slot)
    {
        lock (_gate)
        {
            return _locations.TryGetValue(module, out var slots)
                   && slot >= 1 && slot <= slots.Length
                   && slots[slot - 1] is null;
        }
    }

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

    public WaferInfo? FindById(Guid id)
    {
        lock (_gate)
        {
            foreach (var slots in _locations.Values)
            {
                foreach (var wafer in slots)
                {
                    if (wafer is not null && wafer.Id == id)
                    {
                        return wafer.Clone();
                    }
                }
            }

            return null;
        }
    }

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
    /// 拿设备实际在位跟账核对
    /// </summary>
    /// <param name="module"></param>
    /// <param name="slot"></param>
    /// <param name="deviceHasWafer"></param>
    /// <returns></returns>
    public bool Verify(string module, int slot, bool deviceHasWafer)
    {
        bool onLedger = HasWafer(module, slot);
        if (onLedger == deviceHasWafer)
        {
            return true;
        }

        Fault(onLedger? $"{module}.{slot:00} 账上有片，设备上没有": $"{module}.{slot:00} 设备上有片，账上没有");
        return false;
    }

    #endregion

    #region 人工调整（设置 → 账单调整页）

    /// <summary>
    /// 调整记录最多给界面列几条（新的在前）。
    /// </summary>
    public const int RecentAdjustmentCount = 50;

    private readonly object _adjustmentGate = new();

    /// <summary>
    /// 最近的人工调整（新的在前）：流水不落库时就靠它；落库时以库里的为准。
    /// </summary>
    private readonly List<WaferAdjustmentEntity> _recentAdjustments = [];

    private bool _adjustmentTableReady;

    /// <summary>
    /// 人工移账
    /// </summary>
    /// <param name="fromModule"></param>
    /// <param name="fromSlot"></param>
    /// <param name="toModule"></param>
    /// <param name="toSlot"></param>
    /// <param name="operatorName"></param>
    /// <param name="reason"></param>
    /// <returns></returns>
    public WaferAdjustResult ManualMove(string fromModule, int fromSlot, string toModule, int toSlot, string operatorName, string? reason)
    {
        if (!IsEnable)
        {
            return WaferAdjustResult.Disabled;
        }

        WaferInfo moved;
        lock (_gate)
        {
            if (!_locations.TryGetValue(fromModule, out var source) || !_locations.TryGetValue(toModule, out var target))
            {
                return WaferAdjustResult.LocationNotFound;
            }

            if (fromSlot < 1 || fromSlot > source.Length || toSlot < 1 || toSlot > target.Length)
            {
                return WaferAdjustResult.SlotOutOfRange;
            }

            var wafer = source[fromSlot - 1];
            if (wafer is null)
            {
                return WaferAdjustResult.NoWafer;
            }

            if (ReferenceEquals(source, target) && fromSlot == toSlot)
            {
                return WaferAdjustResult.SameSlot;
            }

            if (target[toSlot - 1] is not null)
            {
                return WaferAdjustResult.SlotOccupied;
            }

            source[fromSlot - 1] = null;
            target[toSlot - 1] = wafer;
            wafer.Module = toModule;
            wafer.Slot = toSlot;
            wafer.Path = NextPath(wafer.Path, toModule);
            wafer.UpdatedAt = DateTime.Now;
            moved = wafer;
        }

        var snapshot = moved.Clone();
        WaferMoved?.Invoke(snapshot, fromModule, fromSlot);
        RecordHistory(WaferHistoryAction.Moved, snapshot, fromModule, fromSlot);
        RecordAdjustment("Move", snapshot, fromModule, fromSlot, toModule, toSlot, operatorName, reason);
        LogHelper.Info(this, $"人工移账：{snapshot.WaferId} {fromModule}.{fromSlot:00} → {toModule}.{toSlot:00}，操作人 {operatorName}"
            + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $"，原因：{reason}"));
        return WaferAdjustResult.Ok;
    }

    /// <summary>
    /// 人工补账
    /// </summary>
    /// <param name="module"></param>
    /// <param name="slot"></param>
    /// <param name="waferId"></param>
    /// <param name="operatorName"></param>
    /// <param name="reason"></param>
    /// <returns></returns>
    public WaferAdjustResult ManualCreate(string module, int slot, string waferId, string operatorName, string? reason)
    {
        if (!IsEnable)
        {
            return WaferAdjustResult.Disabled;
        }

        string id = waferId.Trim();
        if (id.Length == 0)
        {
            return WaferAdjustResult.WaferIdRequired;
        }

        WaferInfo created;
        lock (_gate)
        {
            if (!_locations.TryGetValue(module, out var slots))
            {
                return WaferAdjustResult.LocationNotFound;
            }

            if (slot < 1 || slot > slots.Length)
            {
                return WaferAdjustResult.SlotOutOfRange;
            }

            if (slots[slot - 1] is not null)
            {
                return WaferAdjustResult.SlotOccupied;
            }

            foreach (var location in _locations.Values)
            {
                foreach (var wafer in location)
                {
                    if (wafer is not null && string.Equals(wafer.WaferId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        return WaferAdjustResult.DuplicateWaferId;
                    }
                }
            }

            created = new WaferInfo(module, slot, WaferStatus.Normal, null, null, _loadPorts.Contains(module));
            created.WaferId = id;
            slots[slot - 1] = created;
        }

        var snapshot = created.Clone();
        WaferCreated?.Invoke(snapshot);
        RecordHistory(WaferHistoryAction.Created, snapshot);
        RecordAdjustment("Create", snapshot, module, slot, null, null, operatorName, reason);
        LogHelper.Info(this, $"人工补账：{snapshot.WaferId} {module}.{slot:00}，操作人 {operatorName}"
            + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $"，原因：{reason}"));
        return WaferAdjustResult.Ok;
    }

    /// <summary>
    /// 人工删账
    /// </summary>
    /// <param name="module"></param>
    /// <param name="slot"></param>
    /// <param name="operatorName"></param>
    /// <param name="reason"></param>
    /// <returns></returns>
    public WaferAdjustResult ManualDelete(string module, int slot, string operatorName, string? reason)
    {
        if (!IsEnable)
        {
            return WaferAdjustResult.Disabled;
        }

        WaferInfo deleted;
        lock (_gate)
        {
            if (!_locations.TryGetValue(module, out var slots))
            {
                return WaferAdjustResult.LocationNotFound;
            }

            if (slot < 1 || slot > slots.Length)
            {
                return WaferAdjustResult.SlotOutOfRange;
            }

            var wafer = slots[slot - 1];
            if (wafer is null)
            {
                return WaferAdjustResult.NoWafer;
            }

            deleted = wafer;
            deleted.UpdatedAt = DateTime.Now;
            slots[slot - 1] = null;
        }

        var snapshot = deleted.Clone();
        WaferDeleted?.Invoke(snapshot);
        RecordHistory(WaferHistoryAction.Deleted, snapshot);
        RecordAdjustment("Delete", snapshot, module, slot, null, null, operatorName, reason);
        LogHelper.Info(this, $"人工删账：{snapshot.WaferId} {module}.{slot:00}，操作人 {operatorName}"
            + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $"，原因：{reason}"));
        return WaferAdjustResult.Ok;
    }

    /// <summary>
    /// 最近的人工调整记录
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<WaferAdjustmentEntity> GetRecentAdjustments()
    {
        if (_historyRows is not null)
        {
            try
            {
                using var db = XyzDb.Create(HistoryDatabase);
                EnsureAdjustmentTable(db);
                return db.Queryable<WaferAdjustmentEntity>()
                    .OrderBy(row => row.OccurredAt, OrderByType.Desc)
                    .Take(RecentAdjustmentCount)
                    .ToList();
            }
            catch (Exception exception)
            {
                LogHelper.Warn(this, $"查人工调整记录失败，给本次运行里的: {exception.Message}");
            }
        }

        lock (_adjustmentGate)
        {
            return _recentAdjustments.ToList();
        }
    }

    /// <summary>
    /// 记一条人工调整：内存里留最近的；流水落库时也写进库
    /// </summary>
    /// <param name="action"></param>
    /// <param name="wafer"></param>
    /// <param name="fromModule"></param>
    /// <param name="fromSlot"></param>
    /// <param name="toModule"></param>
    /// <param name="toSlot"></param>
    /// <param name="operatorName"></param>
    /// <param name="reason"></param>
    private void RecordAdjustment(string action, WaferInfo wafer, string fromModule, int fromSlot,
        string? toModule, int? toSlot, string operatorName, string? reason)
    {
        // 映射元件开机准备时取、开机恢复时查过在；没走开机准备的（冒烟直接 new 的）才会是 null
        if (_mapper is null)
        {
            return;
        }

        var row = _mapper.Map<WaferAdjustmentEntity>(wafer);
        row.Action = action;
        row.FromModule = fromModule;
        row.FromSlot = fromSlot;
        row.ToModule = toModule;
        row.ToSlot = toSlot;
        row.Operator = operatorName;
        row.Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        row.OccurredAt = DateTime.Now;

        lock (_adjustmentGate)
        {
            _recentAdjustments.Insert(0, row);
            if (_recentAdjustments.Count > RecentAdjustmentCount)
            {
                _recentAdjustments.RemoveAt(_recentAdjustments.Count - 1);
            }
        }

        if (_historyRows is null)
        {
            return;
        }

        try
        {
            using var db = XyzDb.Create(HistoryDatabase);
            EnsureAdjustmentTable(db);
            db.Insertable(row).ExecuteCommand();
        }
        catch (Exception exception)
        {
            LogHelper.Warn(this, $"人工调整记录写库失败（账已经改了）: {exception.Message}");
        }
    }

    /// <summary>
    /// 调整记录表第一次用到时建（已有就只补列）。
    /// </summary>
    private void EnsureAdjustmentTable(ISqlSugarClient db)
    {
        if (_adjustmentTableReady)
        {
            return;
        }

        db.CodeFirst.InitTables<WaferAdjustmentEntity>();
        _adjustmentTableReady = true;
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
    /// 账实不符：记日志并报警
    /// </summary>
    /// <param name="message"></param>
    private void Fault(string message)
    {
        LogHelper.Error(this, $"晶圆账: {message}");
        RaiseAlarm(WaferLedgerAlarm);
    }

    #region 过期清理 一天检测一次

    private DateTime _historyCleanedDay = DateTime.MinValue;

    /// <summary>
    /// 清理
    /// </summary>
    private void CleanupIfDayChanged()
    {
        if (_historyCleanedDay == DateTime.Today)
        {
            return;
        }

        _historyCleanedDay = DateTime.Today;
        if (!XyzDb.IsRegistered(HistoryDatabase))
        {
            LogHelper.Warn(this, $"晶圆流水库 {HistoryDatabase} 没在 machine.xml 的 Database 节点里配，落到默认库");
        }

        CleanupHistory();
    }

    /// <summary>
    /// 清理过期：按天分表，直接删整张过期的日表
    /// </summary>
    private void CleanupHistory()
    {
        try
        {
            var deadline = DateTime.Today.AddDays(-HistoryKeepDays); //machine.xml 配置
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
                LogHelper.Info(this, $"清理晶圆流水 {expired.Count} 张过期日表（保留 {HistoryKeepDays} 天）");
            }

            if (db.DbMaintenance.IsAnyTable("wafer_adjustment", false))
            {
                db.Deleteable<WaferAdjustmentEntity>().Where(row => row.OccurredAt < deadline).ExecuteCommand();
            }
        }
        catch (Exception exception)
        {
            LogHelper.Warn(this, $"清理晶圆流水失败: {exception.Message}");
        }
    }

    #endregion

    /// <summary>
    /// 宿主退出：账最后存一次盘，下次开机按它把腔体、机械手上的片放回去
    /// </summary>
    public override void OnStopping()
    {
        StopSnapshot();
    }
}
