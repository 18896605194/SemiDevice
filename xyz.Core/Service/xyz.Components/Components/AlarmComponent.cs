using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;
using SqlSugar;
using xyz.Common.Log;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Configs.Models;
using xyz.Database.Alarms;
using xyz.Database.DbProvider;

namespace xyz.Components.Components;

/// <summary>
/// 报警管理：全系统一个，只管三件事——当前有哪些报警、人工清除、报警入库。
/// 报警由各组件经基类 RaiseAlarm / CheckAlarm 自己报；报出去以后只能人工复位清（走组件的 Reset），
/// 源头恢复了也不自动清。不控制模块动作、四色灯或蜂鸣器。
/// </summary>
[Component(description: "报警管理组件")]
public class AlarmComponent : ComponentBase, IAlarmComponent
{
    /// <summary>
    /// 当前报警管理；sc.xml 里装出来即生效。没装（冒烟、单测）时各组件报警是空操作，不影响设备跑。
    /// </summary>
    public static AlarmComponent? Current { get; set; }

    private readonly object _syncRoot = new();
    private readonly Dictionary<(string SourcePath, string AlarmCode), AlarmAttribute> _definitions = new();
    private readonly Dictionary<(string SourcePath, string AlarmCode), AlarmItem> _activeAlarms = new();

    /// <summary>
    /// 报过报警的组件：来源路径 → 组件。第一次报时读它身上的 [Alarm] 定义，人工复位时按来源找回它。
    /// </summary>
    private readonly Dictionary<string, ComponentBase> _sources = new(StringComparer.Ordinal);

    private readonly Queue<AlarmItem> _notifications = new();
    private bool _publishing;

    public AlarmComponent()
    {
        Current = this;
    }

    #region SC

    [SCEditor("True", "Alarm", "是否把报警记录入库（False=只在内存里记当前报警）")]
    public bool EnableHistory { get; set; } = true;

    [SCEditor("Default", "Alarm", "报警记录落哪个库（sc.xml 的 Database 节点名），报警属业务数据，走默认库")]
    public string HistoryDatabase { get; set; } = XyzDb.DefaultName;

    [SCEditor("90", "Alarm", "报警记录保留天数，超期每天清理一次")]
    public int HistoryKeepDays { get; set; } = 90;

    /// <summary>
    /// 报警历史一次最多返回条数的默认值（sc.xml 没配或没装报警组件时用）。
    /// </summary>
    public const int DefaultHistoryQueryMaxCount = 1000;

    [SCEditor("1000", "Alarm", "报警历史页一次最多返回多少条（取最新的），超出界面提示缩小时间段或加条件")]
    public int HistoryQueryMaxCount { get; set; } = DefaultHistoryQueryMaxCount;

    #endregion

    /// <summary>
    /// 报警变化的快照：报出、清除各推一条；重复报不推。
    /// 按变化顺序分发，不持有状态锁，也不保证在 UI 线程执行。
    /// 订阅者应及时返回，异常写入 Trace，不影响其他订阅者和已经完成的状态变更。
    /// </summary>
    public event Action<AlarmItem>? AlarmChanged;

    /// <summary>当前报警的快照，按报出时间排序。</summary>
    public IReadOnlyList<AlarmItem> ActiveAlarms
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeAlarms.Values
                    .OrderBy(alarm => alarm.RaisedAt)
                    .Select(alarm => alarm.Snapshot())
                    .ToArray();
            }
        }
    }

    #region 组件上报（只经 ComponentBase：组件报自己的，Reset 时清自己的）

    /// <summary>
    /// 报一条报警：来源路径与报警定义都从组件自己身上取，不用事先注册。已经在报时返回 false，保留首次报出时间。
    /// 上报本身不能把调用方带崩——这些都在设备扫描线程上调，定义配错了（报警码重复、为空、没有这条）只记一条日志，设备照跑。
    /// </summary>
    internal bool Raise(ComponentBase source, string alarmCode)
    {
        try
        {
            string path = EnsureRegistered(source);
            lock (_syncRoot)
            {
                var key = (path, alarmCode);
                if (_activeAlarms.ContainsKey(key))
                {
                    return false;
                }

                if (!_definitions.TryGetValue(key, out var definition))
                {
                    throw new InvalidOperationException($"{path} 上没有报警 {alarmCode} 的 [Alarm] 定义。");
                }

                var alarm = new AlarmItem
                {
                    SourcePath = path,
                    AlarmCode = alarmCode,
                    AlarmText = definition.AlarmText,
                    Category = definition.Category,
                    Level = definition.AlarmLevel,
                    Description = definition.Description,
                    Solution = definition.Solution,
                    RaisedAt = DateTimeOffset.UtcNow
                };

                _activeAlarms.Add(key, alarm);
                _notifications.Enqueue(alarm.Snapshot());
            }
        }
        catch (Exception exception)
        {
            LogHelper.Warn(Name, $"报警 {alarmCode} 上报失败: {exception.Message}");
            return false;
        }

        PublishChanges();
        return true;
    }

    /// <summary>
    /// 清掉这个组件自己的全部报警（组件 Reset 时调；子组件的由子组件自己的 Reset 清）。返回清了几条。
    /// </summary>
    internal int Clear(ComponentBase source)
    {
        string path = source.AlarmSource;
        int cleared = 0;
        lock (_syncRoot)
        {
            foreach (var key in _activeAlarms.Keys.Where(key => key.SourcePath == path).ToArray())
            {
                _activeAlarms.Remove(key, out var alarm);
                alarm!.ClearedAt = DateTimeOffset.UtcNow;
                _notifications.Enqueue(alarm.Snapshot());
                cleared++;
            }
        }

        if (cleared > 0)
        {
            LogHelper.Info(Name, $"{path} 复位，清除报警 {cleared} 条");
            PublishChanges();
        }

        return cleared;
    }

    /// <summary>
    /// 这个路径的组件自己或它下面的子组件有没有报警。
    /// </summary>
    internal bool HasAlarmUnder(string path)
    {
        lock (_syncRoot)
        {
            return _activeAlarms.Keys.Any(key => IsInScope(key.SourcePath, path));
        }
    }

    /// <summary>
    /// 第一次报某个组件的报警时，读它身上的 [Alarm] 定义并记下它；同一个实例读过就不再读。
    /// 同一路径换了实例（重新装配）以新实例为准。
    /// </summary>
    private string EnsureRegistered(ComponentBase source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string path = source.AlarmSource;
        lock (_syncRoot)
        {
            if (_sources.TryGetValue(path, out var known) && ReferenceEquals(known, source))
            {
                return path;
            }
        }

        var definitions = ReadDefinitions(path, source);
        lock (_syncRoot)
        {
            foreach (var definition in definitions)
            {
                _definitions[(path, definition.Key)] = definition.Value;
            }

            _sources[path] = source;
        }

        return path;
    }

    /// <summary>
    /// 读组件公开实例字段或属性上的 [Alarm]（含继承的），字符串成员值作为报警代码。代码为空或重复时报错。
    /// </summary>
    private static Dictionary<string, AlarmAttribute> ReadDefinitions(string path, ComponentBase source)
    {
        var definitions = new Dictionary<string, AlarmAttribute>(StringComparer.Ordinal);
        foreach (var member in source.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = member.GetCustomAttribute<AlarmAttribute>(inherit: true);
            if (attribute is null)
            {
                continue;
            }

            string? alarmCode = member switch
            {
                FieldInfo field when field.FieldType == typeof(string) => field.GetValue(source) as string,
                PropertyInfo property when property.PropertyType == typeof(string)
                    && property.GetMethod?.IsPublic == true
                    && property.GetIndexParameters().Length == 0 => property.GetValue(source) as string,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(alarmCode))
            {
                throw new InvalidOperationException($"{path}.{member.Name} 的报警代码必须是可读取的非空字符串。");
            }

            if (!definitions.TryAdd(alarmCode, attribute))
            {
                throw new InvalidOperationException($"{path} 包含重复的报警代码：{alarmCode}。");
            }
        }

        return definitions;
    }

    private static bool IsInScope(string source, string scope)
    {
        return source == scope || source.StartsWith(scope + ".", StringComparison.Ordinal);
    }

    #endregion

    #region 人工复位（界面）

    /// <summary>
    /// 人工复位某个报警来源（连同它的子组件）：走那个组件的 Reset，组件能复位就清掉它的报警。
    /// 没报过报警的来源返回 false。
    /// </summary>
    public bool Reset(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        ComponentBase? source;
        lock (_syncRoot)
        {
            _sources.TryGetValue(sourcePath, out source);
        }

        if (source is null)
        {
            return false;
        }

        source.Reset();
        return true;
    }

    /// <summary>
    /// 人工复位全部有报警的组件；一个复位失败不影响其他的。返回复位了几个组件。
    /// </summary>
    public int ResetAll()
    {
        ComponentBase[] sources;
        lock (_syncRoot)
        {
            sources = _activeAlarms.Keys
                .Select(key => key.SourcePath)
                .Distinct()
                .Select(path => _sources.GetValueOrDefault(path))
                .OfType<ComponentBase>()
                .ToArray();
        }

        foreach (var source in sources)
        {
            try
            {
                source.Reset();
            }
            catch (Exception exception)
            {
                LogHelper.Warn(Name, $"{source.AlarmSource} 复位失败: {exception.Message}");
            }
        }

        return sources.Length;
    }

    #endregion

    #region 报警入库（装配时按 SC 开；直接 new 出来的不碰数据库）

    // ⚠ 这一段里凡是连数据库的（PrepareTodayTable / WriteHistoryLoopAsync / CleanupHistory）
    //   一律不许在 _syncRoot 里调：报警是设备扫描线程报上来的，磁盘一卡，所有设备线程就全堵在报警锁上。
    //   报警变化只入队（RecordHistory），写库在后台线程。

    /// <summary>一次最多攒这么多行再写，避免一行一次 IO。</summary>
    private const int HistoryBatchSize = 64;

    /// <summary>积压到这个条数的整数倍时记一次告警。</summary>
    private const int HistoryBacklogWarning = 1000;

    /// <summary>清理间隔：一天跑一次。</summary>
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    /// <summary>开机第一次清理延后一分钟：让 sc.xml 整棵树装完，别在装配途中连库。</summary>
    private static readonly TimeSpan StartupCleanupDelay = TimeSpan.FromMinutes(1);

    /// <summary>null = 不落库（没装配，或 SC 里关了）。</summary>
    private Channel<AlarmHistoryEntity>? _historyRows;

    private Timer? _cleanupTimer;
    private int _historyPending;
    private DateTime _historyPreparedDay = DateTime.MinValue;

    /// <summary>
    /// 装配读完 SC 后开记录队列与写库线程；EnableHistory=False 就不开，不碰数据库。
    /// 这里不建表：sc.xml 里 Database 节点万一排在后面，这会儿连接还没注册，建表就建到默认库去了。
    /// 等真有记录要写（那时整棵树早装完了）再建。
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        if (!EnableHistory)
        {
            return;
        }

        var rows = Channel.CreateUnbounded<AlarmHistoryEntity>(new UnboundedChannelOptions { SingleReader = true });
        _historyRows = rows;
        _ = Task.Run(() => WriteHistoryLoopAsync(rows));
        _cleanupTimer = new Timer(_ => CleanupHistory(), null, StartupCleanupDelay, CleanupInterval);
    }

    /// <summary>
    /// 停止报警入库（宿主退出时调）：队列关闭、写完剩下的行后线程结束。
    /// </summary>
    public void StopHistory()
    {
        _historyRows?.Writer.TryComplete();
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;
    }

    /// <summary>
    /// 记一条报警记录：只入队，不写库。报出、清除各一行。
    /// </summary>
    private void RecordHistory(AlarmItem alarm)
    {
        if (_historyRows is not { } rows)
        {
            return;
        }

        var row = new AlarmHistoryEntity
        {
            Action = alarm.IsActive ? "Raised" : "Cleared",
            Source = alarm.SourcePath,
            Code = alarm.AlarmCode,
            Text = alarm.AlarmText,
            Category = alarm.Category.ToString(),
            Level = alarm.Level.ToString(),
            RaisedAt = alarm.RaisedAt.LocalDateTime,
            OccurredAt = (alarm.ClearedAt ?? alarm.RaisedAt).LocalDateTime,
        };

        // 队列已关（StopHistory 之后）就当没记，不报错。
        if (!rows.Writer.TryWrite(row))
        {
            return;
        }

        int pending = Interlocked.Increment(ref _historyPending);
        if (pending > 0 && pending % HistoryBacklogWarning == 0)
        {
            LogHelper.Warn(Name, $"报警记录积压 {pending} 行，检查数据库是否卡住");
        }
    }

    /// <summary>
    /// 写库后台线程：攒一批写一次。库挂了只记日志丢这批，报警照报、设备照跑。
    /// </summary>
    private async Task WriteHistoryLoopAsync(Channel<AlarmHistoryEntity> rows)
    {
        var batch = new List<AlarmHistoryEntity>(HistoryBatchSize);
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
                LogHelper.Warn(Name, $"报警记录写库失败，丢弃 {batch.Count} 行: {exception.Message}");
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
            LogHelper.Warn(Name, $"报警记录库 {HistoryDatabase} 没在 sc.xml 的 Database 节点里配，暂时落到默认库");
        }

        using var db = XyzDb.Create(HistoryDatabase);
        db.CodeFirst.SplitTables().InitTables<AlarmHistoryEntity>();
        _historyPreparedDay = DateTime.Today;
    }

    /// <summary>
    /// 清理过期记录：按天分表，直接删整张过期的日表，不用逐行删；失败只记日志，下次再试。
    /// </summary>
    private void CleanupHistory()
    {
        try
        {
            var deadline = DateTime.Today.AddDays(-HistoryKeepDays);
            using var db = XyzDb.Create(HistoryDatabase);
            var expired = db.SplitHelper<AlarmHistoryEntity>().GetTables()
                .Where(table => table.Date < deadline)
                .ToList();

            foreach (var table in expired)
            {
                db.DbMaintenance.DropTable(table.TableName);
            }

            if (expired.Count > 0)
            {
                LogHelper.Info($"[{Name}] 清理报警记录 {expired.Count} 张过期日表（保留 {HistoryKeepDays} 天）");
            }
        }
        catch (Exception exception)
        {
            LogHelper.Warn(Name, $"清理报警记录失败: {exception.Message}");
        }
    }

    #endregion

    /// <summary>
    /// 按变化顺序分发：每条先记入库队列，再通知订阅者。
    /// 状态变更和入队使用同一把锁；仅一个调用方分发，保证并发和回调重入时的通知顺序。
    /// </summary>
    private void PublishChanges()
    {
        lock (_syncRoot)
        {
            if (_publishing)
            {
                return;
            }

            _publishing = true;
        }

        while (true)
        {
            AlarmItem alarm;
            lock (_syncRoot)
            {
                if (_notifications.Count == 0)
                {
                    _publishing = false;
                    return;
                }

                alarm = _notifications.Dequeue();
            }

            RecordHistory(alarm);

            var handlers = AlarmChanged;
            if (handlers is null)
            {
                continue;
            }

            foreach (Action<AlarmItem> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(alarm);
                }
                catch (Exception exception)
                {
                    try
                    {
                        Trace.TraceError($"AlarmChanged 订阅者处理 {alarm.SourcePath}.{alarm.AlarmCode} 失败：{exception}");
                    }
                    catch
                    {
                        // 诊断监听器自身失败时，也不能阻断后续报警通知。
                    }
                }
            }
        }
    }
}
