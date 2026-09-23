using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;
using SqlSugar;
using xyz.Common.Log;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Configs.Models;
using xyz.Database.Alarms;
using xyz.Database.DbProvider;

namespace xyz.Components.Components;

/// <summary>
/// 报警管理：全系统一个，只管三件事——当前有哪些报警、人工清除、报警入库。
/// </summary>
[Component(description: "报警管理组件")]
public class AlarmComponent : ComponentBase, IAlarmComponent
{
    public static AlarmComponent? Current { get; set; }

    private readonly object _syncRoot = new();

    /// <summary>
    /// 报警记录
    /// </summary>
    private readonly Dictionary<(string SourcePath, string AlarmCode), AlarmAttribute> _definitions = new();

    /// <summary>
    /// 实时报警
    /// </summary>
    private readonly Dictionary<(string SourcePath, string AlarmCode), AlarmItem> _activeAlarms = new();

    /// <summary>
    /// 报过报警的组件：人工复位时按来源找回它。
    /// </summary>
    private readonly Dictionary<string, ComponentBase> _sources = new(StringComparer.Ordinal);

    /// <summary>
    /// 报警通知 队列
    /// </summary>
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

    [SCEditor("1000", "Alarm", "报警历史页一次最多返回多少条（取最新的），超出界面提示缩小时间段或加条件")]
    public int HistoryQueryMaxCount { get; set; } = 1000;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "条", "1", "1000", "64", "报警记录入库批量：写库线程每次最多攒这么多条写一次")]
    public int HistoryBatchSize
    {
        get { return GetEcInt(nameof(HistoryBatchSize)); }
        set { SetEcInt(nameof(HistoryBatchSize), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "条", "100", "100000", "1000", "入库积压告警：待写库的记录攒到这个条数的整数倍时记一次告警日志")]
    public int HistoryBacklogWarning
    {
        get { return GetEcInt(nameof(HistoryBacklogWarning)); }
        set { SetEcInt(nameof(HistoryBacklogWarning), value); }
    }

    #endregion

    /// <summary>
    /// 报警变化的快照 事件
    /// </summary>
    public event Action<AlarmItem>? AlarmChanged;

    /// <summary>
    /// 当前报警的快照，按报出时间排序。
    /// </summary>
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
    /// 触发报警
    /// </summary>
    /// <param name="source"></param>
    /// <param name="alarmCode"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    internal bool Raise(ComponentBase source, string alarmCode)
    {
        try
        {
            string path = EnsureRegistered(source); //报警先记住
            lock (_syncRoot)
            {
                var key = (path, alarmCode);
                if (_activeAlarms.ContainsKey(key))
                {
                    return false;
                }
                //先去报警记录里面查询有没有定义
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

                _activeAlarms.Add(key, alarm); //添加到实时报警里面
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
    /// 清掉这个组件自己的全部报警
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
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
    /// 组件自己或它下面的子组件有没有报警。
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

    #region 报警入库

    private Channel<AlarmHistoryEntity>? _historyRows;

    private int _historyPending;

    /// <summary>
    /// 装配读完 SC 后开记录队列与写库线程；EnableHistory=False 就不开，不碰数据库。
    /// 这里不建表也不连库：日表由实体上的 [SplitTable] 让 ORM 插入时自动建；
    /// sc.xml 里 Database 节点万一排在后面，这会儿连接还没注册，等真有记录要写（那时整棵树早装完了）再碰库。
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        if (!EnableHistory)//是否把报警记录入库
        {
            return;
        }

        var rows = Channel.CreateUnbounded<AlarmHistoryEntity>(new UnboundedChannelOptions { SingleReader = true });
        _historyRows = rows;
        _ = Task.Run(() => WriteHistoryLoopAsync(rows));
    }

    /// <summary>
    /// 停止报警入库（宿主退出时调）：队列关闭、写完剩下的行后线程结束。
    /// </summary>
    public void StopHistory()
    {
        _historyRows?.Writer.TryComplete();
    }

    /// <summary>
    /// 记一条报警记录：只入队，不写库。报出、清除各一行。
    /// </summary>
    private void RecordHistory(AlarmItem alarm)
    {
        var rows = _historyRows;
        if (rows is null)
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
        int backlogWarning = HistoryBacklogWarning;
        if (backlogWarning > 0 && pending > 0 && pending % backlogWarning == 0)
        {
            LogHelper.Warn(Name, $"报警记录积压 {pending} 行，检查数据库是否卡住");
        }
    }

    /// <summary>
    /// 写库循环：攒一批写一次。库挂了只记日志丢这批，报警照报、设备照跑；队列关了（StopHistory）写完剩下的就退出。
    /// </summary>
    private async Task WriteHistoryLoopAsync(Channel<AlarmHistoryEntity> rows)
    {
        var batch = new List<AlarmHistoryEntity>();
        // 有行进来才醒，队列关了返回 false。
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
                CleanupIfDayChanged();  //写库之前check一下需要删除的历史数据
                // 当天的日表不用建：实体上的 [SplitTable] 让 ORM 按 OccurredAt 分流、缺表自动建。
                using var db = XyzDb.Create(HistoryDatabase);
                db.Insertable(batch).SplitTable().ExecuteCommand();
            }
            catch (Exception exception)
            {
                LogHelper.Warn(Name, $"报警记录写库失败，丢弃 {batch.Count} 行: {exception.Message}");
            }
        }
    }

    #endregion

    /// <summary>
    /// 报警入队列，这边一直往外获取
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

            RecordHistory(alarm);  //报警入库

            var handlers = AlarmChanged;
            if (handlers is null)
            {
                continue;
            }

            foreach (Action<AlarmItem> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(alarm);  //一次性处理所有订阅者
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

    #region 过期记录清理（一天一次：跨天后写第一批之前顺手清；平时不用看）

    private DateTime _historyCleanedDay = DateTime.MinValue;

    /// <summary>
    /// 跨天了（含开机第一批）就清一次过期日表；平时只是比一下日期，不碰库。
    /// 不用定时器：有新记录要写、又跨了天，才在写之前清——没记录写就没表在长，也不用清。
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
            LogHelper.Warn(Name, $"报警记录库 {HistoryDatabase} 没在 sc.xml 的 Database 节点里配，落到默认库");
        }

        CleanupHistory();
    }

    /// <summary>
    /// 清理过期记录：按天分表，直接删整张过期的日表，不用逐行删；失败只记日志，下次跨天再试。
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
}
