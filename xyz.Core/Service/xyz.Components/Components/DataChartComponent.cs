using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.DataCharts;
using xyz.Components.Enums;
using xyz.Components.Io;
using xyz.Database.DbProvider;

namespace xyz.Components.Components;

/// <summary>
/// 数据曲线：每个采样周期（默认 1 秒）采一整行——全部 IO 点 + 能画成曲线的 SV，值没变也记——入库，
/// 供数据曲线页按名字、按时间查。同一份采样经 <see cref="Records"/> 流交给实时曲线（RealChartComponent）推给界面：
/// 采样只有这一处，历史和实时看到的是同一份数据，PLC 也不用多读一遍。
///
/// 库表见 <see cref="DataRecordTable"/>：按天一张宽表，一行 = 时间 + 全部信号值，列名就是信号名；读不到的值存空，不存 0。
/// 管道（Rx）：采样线程每周期 OnNext 一行 →
///   ① 本组件：按 RecordBatchSeconds 攒一批 → 写库线程逐批写（写库慢了只积压，不拖采样）；
///   ② 实时曲线：留最近一段 + 推给界面。
/// </summary>
[Component(description: "数据曲线（每秒采样入库，按名字、按时间查询）")]
public class DataChartComponent : ComponentBase
{
    public static DataChartComponent? Current { get; set; }

    public DataChartComponent()
    {
        Current = this;
    }

    #region SC

    [SCEditor("True", "DataChart", "是否入库（False = 只采样给实时曲线，不写库，数据曲线页查不到新数据）")]
    public bool EnableRecord { get; set; } = true;

    [SCEditor("Io", "DataChart", "落哪个库（sc.xml 的 Database 节点名）：秒级不停写，走单独的 Io 库，不跟业务数据挤")]
    public string Database { get; set; } = "Io";

    [SCEditor("30", "DataChart", "保留天数：按天一张表，超期的整张删，每天清一次")]
    public int KeepDays { get; set; } = 30;

    [SCEditor("1000", "DataChart", "采样周期（毫秒），按整周期对齐；每个周期整行都记，值没变也记")]
    public int SampleIntervalMs { get; set; } = 1000;

    [SCEditor("", "DataChart", "不采的信号，逗号分隔：写信号名，或写上级路径整枝不采（如 LoadPort1.E84）")]
    public string Exclude { get; set; } = string.Empty;

    [SCEditor("4000", "DataChart", "数据曲线页一次查询每条曲线最多返回多少点；时间段长了按时段取最小、最大值，峰谷不丢")]
    public int QueryMaxPoints { get; set; } = 4000;

    [SCEditor("20", "DataChart", "数据曲线、实时曲线页最多同时画几条曲线")]
    public int QueryMaxSignals { get; set; } = 20;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "s", "1", "60", "5", "入库批量：攒这么多秒的行写一次库")]
    public int RecordBatchSeconds
    {
        get { return GetEcInt(nameof(RecordBatchSeconds)); }
        set { SetEcInt(nameof(RecordBatchSeconds), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "行", "10", "100000", "300", "入库积压告警：待写库的行攒到这个数的整数倍时记一次告警日志")]
    public int RecordBacklogWarning
    {
        get { return GetEcInt(nameof(RecordBacklogWarning)); }
        set { SetEcInt(nameof(RecordBacklogWarning), value); }
    }

    #endregion

    #region 采样

    private readonly Subject<DataRecord> _records = new();

    private IReadOnlyList<DataSignal> _signals = [];

    private CancellationTokenSource? _sampling;

    private Task? _samplingTask;

    /// <summary>
    /// 在采的信号，顺序即每行值的顺序；StartSampling 之前为空，运行期不变。
    /// </summary>
    public IReadOnlyList<DataSignal> Signals => _signals;

    /// <summary>
    /// 每个采样周期一行，在采样线程上发出；订阅方别在回调里干慢活，要干就 ObserveOn 到自己的线程。
    /// </summary>
    public IObservable<DataRecord> Records => _records.AsObservable();

    /// <summary>
    /// 装配全部完成后（SV 编号表合并完、IO 点表读好）调一次：定信号表、开写库管道、起采样线程。
    /// 不叫 Start：基类的 Start 是模块扫描线程，这里不走扫描。
    /// </summary>
    public void StartSampling()
    {
        if (_sampling is not null)
        {
            return;
        }

        _signals = BuildSignals();
        int svCount = _signals.Count(signal => signal.Source == "SV");
        LogHelper.Info(Name, $"数据曲线：每 {SampleIntervalMs}ms 采一行，{_signals.Count} 个信号（SV {svCount}、IO {_signals.Count - svCount}）"
            + (EnableRecord ? $"，入 {Database} 库、保留 {KeepDays} 天" : "，不入库（EnableRecord=False）"));

        if (EnableRecord)
        {
            StartWriter();
        }

        _sampling = new CancellationTokenSource();
        var token = _sampling.Token;
        _samplingTask = Task.Factory.StartNew(() => SampleLoop(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// 宿主退出时调：停采样，把攒着的最后一批写完（最多等几秒）。
    /// </summary>
    public void StopSampling()
    {
        if (_sampling is null)
        {
            return;
        }

        _sampling.Cancel();
        _samplingTask?.Wait(TimeSpan.FromSeconds(2));
        _records.OnCompleted();
        _writerDone?.Task.Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// 信号表：先 SV（编号表顺序），再 IO（DI、DO、AI、AO，点表顺序）；Exclude 里的不要，重名的第二个改名并告警。
    /// </summary>
    private IReadOnlyList<DataSignal> BuildSignals()
    {
        var excludes = Exclude.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var signals = new List<DataSignal>();

        void Add(DataSignal signal)
        {
            if (excludes.Any(pattern => signal.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase)
                                        || signal.Name.StartsWith(pattern + ".", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (!names.Add(signal.Name))
            {
                string renamed = $"{signal.Name}_{signal.Source}";
                LogHelper.Warn(Name, $"数据曲线信号 {signal.Name} 重名，{signal.Source} 这个改记成 {renamed}（改一下点表的 Tag 列）");
                if (!names.Add(renamed))
                {
                    return;
                }

                signal = signal with { Name = renamed };
            }

            signals.Add(signal);
        }

        foreach (var sv in GemCollectors.Current?.Sv.NumericItems() ?? [])
        {
            Add(new DataSignal(sv.Name, sv.Format == ValueFormat.Bool, "SV", sv.Unit, sv.Description, sv.Read));
        }

        if (IoComponent.Current is { } io)
        {
            foreach (var point in io.Di.Points)
            {
                Add(IoSignal(point, "DI", true, () => io.TryReadDi(point.Index, out bool on) ? on ? 1 : 0 : null));
            }

            foreach (var point in io.Do.Points)
            {
                Add(IoSignal(point, "DO", true, () => io.TryReadDo(point.Index, out bool on) ? on ? 1 : 0 : null));
            }

            foreach (var point in io.Ai.Points)
            {
                Add(IoSignal(point, "AI", false, () => io.TryReadAi(point.Index, out double value) ? value : null));
            }

            foreach (var point in io.Ao.Points)
            {
                Add(IoSignal(point, "AO", false, () => io.TryReadAo(point.Index, out double value) ? value : null));
            }
        }

        if (signals.Count > DataRecordTable.MaxSignals)
        {
            LogHelper.Error(Name, $"数据曲线信号 {signals.Count} 个，超过一张表的列数上限，只记前 {DataRecordTable.MaxSignals} 个（用 Exclude 去掉不要的）");
            signals.RemoveRange(DataRecordTable.MaxSignals, signals.Count - DataRecordTable.MaxSignals);
        }

        return signals;
    }

    /// <summary>
    /// IO 点的信号名 = Module.Component.Tag（Tag 没填用点名），跟 SV 的"组件全路径.属性名"落在同一套层级上。
    /// </summary>
    private static DataSignal IoSignal(IoPoint point, string source, bool isDigital, Func<double?> read)
    {
        string property = point.Tag.Length > 0 ? point.Tag : point.Name;
        string name = string.Join(".", new[] { point.Module, point.Component, property }.Where(part => part.Length > 0));
        return new DataSignal(name, isDigital, source, point.Unit, point.Description, read);
    }

    /// <summary>
    /// 采样线程：按整周期对齐（1 秒周期就是整秒），一个周期采一整行。
    /// 落后超过一个周期（机器卡住、系统时间往前调）就跳到下一个整周期，不补；系统时间往回调了也重新对齐。
    /// </summary>
    private void SampleLoop(CancellationToken token)
    {
        long interval = Math.Max(100, SampleIntervalMs);
        long next = AlignUp(NowMs(), interval);
        string? lastFault = null;
        while (true)
        {
            long wait = next - NowMs();
            if (wait > interval)
            {
                next = AlignUp(NowMs(), interval);
                wait = next - NowMs();
            }

            if (wait > 0 && token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(wait)))
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var values = new double?[_signals.Count];
                for (int index = 0; index < values.Length; index++)
                {
                    values[index] = _signals[index].Read();
                }

                _records.OnNext(new DataRecord(next, values));
                lastFault = null;
            }
            catch (Exception exception)
            {
                // 订阅方抛出来的也兜住：采样线程不能死。同样的错只记一次。
                if (lastFault != exception.Message)
                {
                    lastFault = exception.Message;
                    LogHelper.Warn(Name, $"数据曲线采样出错: {exception.Message}");
                }
            }

            next += interval;
            long now = NowMs();
            if (now - next >= interval)
            {
                next = AlignUp(now, interval);
            }
        }
    }

    private static long NowMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static long AlignUp(long time, long interval)
    {
        return (time / interval + 1) * interval;
    }

    #endregion

    #region 入库

    private TaskCompletionSource? _writerDone;

    private int _pending;

    /// <summary>
    /// 已核对过列的日表：一张表每次开机只核对一次（缺表建表、缺列补列）。
    /// </summary>
    private readonly HashSet<string> _checkedTables = new(StringComparer.OrdinalIgnoreCase);

    private bool _databasePrepared;

    /// <summary>
    /// 写库管道：攒 RecordBatchSeconds 秒（EC，改了下一批生效）→ 写库线程逐批写。
    /// 写库线程只有一条，批与批之间不并发；库挂了只丢这一批、记日志，采样照采、实时曲线照推。
    /// </summary>
    private void StartWriter()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _writerDone = done;
        var writerThread = new EventLoopScheduler(start => new Thread(start) { Name = "DataChart.Writer", IsBackground = true });

        _records
            .Buffer(() => Observable.Timer(TimeSpan.FromSeconds(Math.Clamp(RecordBatchSeconds, 1, 60))))
            .Where(batch => batch.Count > 0)
            .Do(CountPending)
            .ObserveOn(writerThread)
            .Subscribe(
                WriteBatch,
                exception =>
                {
                    LogHelper.Error(Name, $"数据曲线写库管道异常结束: {exception.Message}");
                    done.TrySetResult();
                },
                () => done.TrySetResult());
    }

    private void CountPending(IList<DataRecord> batch)
    {
        int before = Interlocked.Add(ref _pending, batch.Count) - batch.Count;
        int warning = RecordBacklogWarning;
        if (warning > 0 && before / warning != (before + batch.Count) / warning)
        {
            LogHelper.Warn(Name, $"数据曲线待写库积压 {before + batch.Count} 行，检查数据库是否卡住");
        }
    }

    private void WriteBatch(IList<DataRecord> batch)
    {
        try
        {
            CleanupIfDayChanged();
            using var db = XyzDb.Create(Database);
            PrepareDatabase(db);
            foreach (var day in batch.GroupBy(record => DayOf(record.Time)))
            {
                string table = DataRecordTable.NameOf(day.Key);
                EnsureColumns(db, table);
                DataRecordTable.Insert(db, table, _signals, day);
            }
        }
        catch (Exception exception)
        {
            LogHelper.Warn(Name, $"数据曲线写库失败，丢弃 {batch.Count} 行: {exception.Message}");
            // 表可能被人动过（删表、删列），下一批重新核对。
            _checkedTables.Clear();
        }
        finally
        {
            Interlocked.Add(ref _pending, -batch.Count);
        }
    }

    /// <summary>
    /// 开机头一批写之前：库切到 WAL（持久在库文件里）——查询读库时不挡写库，写库也不挡查询。
    /// </summary>
    private void PrepareDatabase(SqlSugar.ISqlSugarClient db)
    {
        if (_databasePrepared)
        {
            return;
        }

        if (!XyzDb.IsRegistered(Database))
        {
            LogHelper.Warn(Name, $"数据曲线库 {Database} 没在 sc.xml 的 Database 节点里配，落到默认库");
        }

        db.Ado.ExecuteCommand("PRAGMA journal_mode=WAL");
        _databasePrepared = true;
    }

    /// <summary>
    /// 日表缺就建（带全部信号列），有就把缺的信号列补上。
    /// </summary>
    private void EnsureColumns(SqlSugar.ISqlSugarClient db, string table)
    {
        if (_checkedTables.Contains(table))
        {
            return;
        }

        var existing = DataRecordTable.ColumnsOf(db, table);
        if (existing.Count == 0)
        {
            DataRecordTable.Create(db, table, _signals);
        }
        else
        {
            var known = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = _signals.Where(signal => !known.Contains(signal.Name)).ToList();
            foreach (var signal in missing)
            {
                DataRecordTable.AddColumn(db, table, signal);
            }

            if (missing.Count > 0)
            {
                LogHelper.Info(Name, $"数据曲线表 {table} 补了 {missing.Count} 列（新加的信号）");
            }
        }

        _checkedTables.Add(table);
    }

    /// <summary>
    /// UTC 毫秒 → 本机日期：日表按本机日期分。
    /// </summary>
    private static DateTime DayOf(long time)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(time).LocalDateTime.Date;
    }

    #endregion

    #region 过期清理（一天一次：跨天后写第一批之前顺手清）

    private DateTime _cleanedDay = DateTime.MinValue;

    /// <summary>
    /// 按天分表，过期的整张删，不用逐行删；删掉的空间 SQLite 留着给新表用，库文件不会一直涨。失败只记日志，下次跨天再试。
    /// </summary>
    private void CleanupIfDayChanged()
    {
        if (_cleanedDay == DateTime.Today)
        {
            return;
        }

        _cleanedDay = DateTime.Today;
        try
        {
            var deadline = DateTime.Today.AddDays(-KeepDays);
            using var db = XyzDb.Create(Database);
            var expired = DataRecordTable.ListTables(db).Where(table => table.Key < deadline).ToList();
            foreach (var table in expired)
            {
                DataRecordTable.Drop(db, table.Value);
            }

            if (expired.Count > 0)
            {
                LogHelper.Info(Name, $"清理数据曲线 {expired.Count} 张过期日表（保留 {KeepDays} 天）");
            }
        }
        catch (Exception exception)
        {
            LogHelper.Warn(Name, $"清理数据曲线失败: {exception.Message}");
        }
    }

    #endregion

    #region 查询（数据曲线页）

    /// <summary>
    /// 能查哪些信号：正在采的（按采样顺序）+ 保留期内库里有过、现在不采了的（按名字排）。
    /// </summary>
    public IReadOnlyList<string> RecordedNames()
    {
        var names = _signals.Select(signal => signal.Name).ToList();
        var known = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var history = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        using var db = XyzDb.Create(Database);
        foreach (var table in DataRecordTable.ListTables(db).Values)
        {
            foreach (var column in DataRecordTable.ColumnsOf(db, table))
            {
                if (!known.Contains(column))
                {
                    history.Add(column);
                }
            }
        }

        names.AddRange(history);
        return names;
    }

    /// <summary>
    /// 按名字查 [start, end)（本机时间）的曲线；点数超过 QueryMaxPoints 就抽稀。名字在某天的表里没有，那天就是空的。
    /// </summary>
    public DataQueryResult Query(IReadOnlyList<string> names, DateTime start, DateTime end, CancellationToken token)
    {
        if (names.Count == 0 || end <= start)
        {
            return DataQueryResult.Empty;
        }

        // 界面传的是本机时间，走 gRPC 只保留刻度（Kind 丢了），这里按本机时间认。
        long from = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        long to = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        using var db = XyzDb.Create(Database);
        return DataRecordQuery.Run(db, names, from, to, start.Date, end.AddTicks(-1).Date,
            QueryMaxPoints, Math.Max(100, SampleIntervalMs), token);
    }

    #endregion
}
