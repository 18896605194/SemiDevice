using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text.RegularExpressions;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Collectors;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Database.DbProvider;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// 数据曲线：每个采样周期（默认 1 秒）
/// </summary>
[Component(description: "数据曲线（每秒采样入库，按名字、按时间查询）")]
public class DataChartComponent : ComponentBase, IDataChart
{
    #region SC

    [SCEditor("True", "DataChart", "是否入库（False = 只采样给实时曲线，不写库，数据曲线页查不到新数据）")]
    public bool EnableRecord { get; set; } = true;

    [SCEditor("Io", "DataChart", "落哪个库（sc.xml 的 Database 节点名）：秒级不停写，走单独的 Io 库，不跟业务数据挤")]
    public string Database { get; set; } = "Io";

    [SCEditor("30", "DataChart", "保留天数：按天一张表，超期的整张删，每天清一次")]
    public int KeepDays { get; set; } = 30;

    [SCEditor("1000", "DataChart", "采样周期（毫秒），按整周期对齐；每个周期整行都记，值没变也记")]
    public int SampleIntervalMs { get; set; } = 1000;

    [SCEditor("", "DataChart", "不采的信号，逗号分隔：写信号名，或写上级路径整枝不采（如 LoadPort1__E84，层级用两个下划线隔开）")]
    public string Exclude { get; set; } = string.Empty;

    [SCEditor("4000", "DataChart", "数据曲线页一次查询每条曲线最多返回多少点；时间段长了按时段取最小、最大值，峰谷不丢")]
    public int QueryMaxPoints { get; set; } = 4000;

    [SCEditor("20", "DataChart", "数据曲线、实时曲线页最多同时画几条曲线")]
    public int QueryMaxSignals { get; set; } = 20;

    [SCEditor("Screenshots", "DataChart", "数据曲线、实时曲线页截图存到哪个文件夹（客户端那台电脑上）；写相对路径就在客户端程序目录下")]
    public string ScreenshotDirectory { get; set; } = "Screenshots";

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



    /// <summary>
    /// 在采的信号
    /// </summary>
    private IReadOnlyList<DataSignal> _signals = [];
    public IReadOnlyList<DataSignal> Signals => _signals;

    /// <summary>
    /// rx技术写法
    /// </summary>
    private readonly Subject<DataRecord> _records = new();
    /// <summary>
    /// 每个采样周期一行，在扫描线程上发出；订阅方别在回调里干慢活，要干就 ObserveOn 到自己的线程。
    /// </summary>
    public IObservable<DataRecord> Records => _records.AsObservable();


    private volatile bool _bound;

    private readonly object _gate = new();

    /// <summary>
    /// 退出时停了采样
    /// </summary>
    private bool _stopped;

    /// <summary>
    /// 手上没有在采的行
    /// </summary>
    private readonly ManualResetEventSlim _rowDone = new(true);

    /// <summary>
    /// 下一行的时刻（UTC 毫秒，整周期）；0 是还没开始
    /// </summary>
    private long _next;

    private string? _lastFault;

    /// <summary>
    /// 开机（开始扫描之前）：照整棵组件树定信号表、开写库管道
    /// </summary>
    public override void Bind()
    {
        Bind(IocHelper.GetRequiredService<IReadOnlyList<ComponentBase>>());
    }

    /// <summary>
    /// 装配完、开始扫描之前调一次：照组件树定信号表、开写库管道
    /// </summary>
    public void Bind(IReadOnlyList<ComponentBase> roots)
    {
        if (_bound)
        {
            return;
        }

        _signals = BuildSignals(roots);
        int svCount = _signals.Count(signal => signal.Source == "SV");
        LogHelper.Info(Name, $"数据曲线：每 {SampleIntervalMs}ms 采一行，{_signals.Count} 个信号（SV {svCount}、IO {_signals.Count - svCount}）"
            + (EnableRecord ? $"，入 {Database} 库、保留 {KeepDays} 天" : "，不入库（EnableRecord=False）"));
        if (!IsRun)
        {
            LogHelper.Warn(Name, "sc.xml 的 DataChart 节点没配 IsRun=true：不采样，数据曲线、实时曲线都没有新数据");
        }

        if (EnableRecord)
        {
            StartWriter(); //开始写数据库
        }

        _bound = true;
    }

    /// <summary>
    /// 宿主退出时调：停采样，把攒着的最后一批写完（最多等几秒）。
    /// </summary>
    public void StopSampling()
    {
        if (!_bound)
        {
            return;
        }

        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
        }

        // 扫描线程手上那一行发完再收尾，不然收尾以后还会冒出一行
        _rowDone.Wait(TimeSpan.FromSeconds(2));
        _records.OnCompleted();
        _writerDone?.Task.Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// 信号表：照 sc.xml 装出来的组件树按顺序走一遍，每个组件先记它能画成曲线的 SV，再记它绑的 IO——
    /// 顺序、层级都跟 sc.xml 一样。Exclude 里的不要。
    /// </summary>
    private IReadOnlyList<DataSignal> BuildSignals(IReadOnlyList<ComponentBase> roots)
    {
        var excludes = Exclude.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var signals = new List<DataSignal>();

        void Add(DataSignal signal)
        {
            if (excludes.Any(pattern => signal.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase)
                                        || signal.Name.StartsWith(pattern + DataSignal.Separator, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // 组件全路径 + 属性名 / 配置项名，本来就唯一；真撞上了（SV 名跟配置项名撞）只记第一个。
            if (!names.Add(signal.Name))
            {
                LogHelper.Warn(Name, $"数据曲线信号 {signal.Name} 重名，{signal.Source} 这个不记");
                return;
            }

            signals.Add(signal);
        }

        var svs = (IocHelper.GetService<GemCollectors>()?.Sv.NumericItems() ?? []).ToLookup(sv => sv.Owner);
        var io = IocHelper.GetService<IIo>();
        bool warnedNoIo = false;
        var levelMeters = new HashSet<int>();
        foreach (var component in CollectorHelper.Walk(roots))
        {
            foreach (var sv in svs[component])
            {
                Add(new DataSignal(SignalName(sv.Name), sv.Format == ValueFormat.Bool, "SV", string.Empty, sv.Unit, sv.Description, sv.Read));
            }

            // 一个液位计（AI）挂在槽的几个液位点上：只记第一个点那一列，不重复记几遍
            if (component is IAiLevel level && !levelMeters.Add(level.AiIndex))
            {
                continue;
            }

            foreach (var binding in IoBindings(component))
            {
                if (io is null)
                {
                    if (!warnedNoIo)
                    {
                        warnedNoIo = true;
                        LogHelper.Warn(Name, "sc.xml 没配 Io 节点：组件绑的 IO 都读不到，数据曲线只记 SV");
                    }

                    break;
                }

                Add(IoSignal(io, binding.Name, binding.Type, binding.Index));
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
    /// sc.xml 里绑 IO 的配置项：int 型的 SC 配置、名字里带 Di / Do / Ai / Ao、以 Index 结尾
    /// （DiOpenedIndex、DoIndex、AiIndex、MonitoringDoIndex、DiValidIndex……），-1 表示没接线。
    /// </summary>
    private static readonly Regex BindingPattern = new("^[A-Za-z]*?(Di|Do|Ai|Ao)[A-Za-z0-9]*Index$", RegexOptions.Compiled);

    /// <summary>
    /// 一个组件在 sc.xml 里绑的 IO：信号名 = 组件全路径 + 配置项名去掉 Index，层级用 __ 隔开；DI 组件只有一个点，信号名就是组件全路径
    /// </summary>
    private static IEnumerable<(string Name, string Type, int Index)> IoBindings(ComponentBase component)
    {
        foreach (var property in component.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(int) || property.GetCustomAttribute<SCEditorAttribute>() is null)
            {
                continue;
            }

            var match = BindingPattern.Match(property.Name);
            if (!match.Success || property.GetValue(component) is not int index || index < 0)
            {
                continue;
            }

            // DI 组件（到位反馈、Safety、Monitor 这些）叫 Door__Opened，不再在后面加一段 __Di
            string path = $"{component.FullPath}.{property.Name[..^"Index".Length]}";
            if (component is IDiSensor && property.Name == "DiIndex")
            {
                path = component.FullPath;
            }

            yield return (SignalName(path), match.Groups[1].Value.ToUpperInvariant(), index);
        }
    }

    /// <summary>
    /// 每一层名字只许字母、数字和单个下划线，不许下划线开头、结尾——不然跟分隔符 __ 分不清，
    /// SqlSugar 拿列名当参数名也会断在别的符号上。
    /// </summary>
    private static readonly Regex SegmentPattern = new("^[A-Za-z0-9]+(_[A-Za-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>
    /// 点号分层的全名（组件路径、SV 全名）→ 信号名（__ 分层），信号名就是库里的列名。哪一层不合规开机就报。
    /// </summary>
    private static string SignalName(string path)
    {
        var parts = path.Split('.');
        foreach (var part in parts)
        {
            if (!SegmentPattern.IsMatch(part))
            {
                throw new InvalidOperationException(
                    $"数据曲线信号 {path} 里的“{part}”不能当列名：只能用字母、数字和单个下划线，不能以下划线开头或结尾（{DataSignal.Separator} 是层级分隔符）");
            }
        }

        return string.Join(DataSignal.Separator, parts);
    }

    /// <summary>
    /// 组件绑的一个 IO 点：地址、单位、说明从点表拿；点表里没有这个点就记告警，这一列会一直是空的。
    /// </summary>
    private DataSignal IoSignal(IIo io, string name, string type, int index)
    {
        IoPoint? point;
        switch (type)
        {
            case "DI":
                point = io.Di.Find(index);
                break;
            case "DO":
                point = io.Do.Find(index);
                break;
            case "AI":
                point = io.Ai.Find(index);
                break;
            default:
                point = io.Ao.Find(index);
                break;
        }

        if (point is null)
        {
            LogHelper.Warn(Name, $"sc.xml 里 {name} 绑了 {type}{index}，点表里没有这个点，数据曲线这一列会一直是空的");
        }

        Func<double?> read;
        switch (type)
        {
            case "DI":
                read = () => io.TryReadDi(index, out bool on) ? on ? 1 : 0 : null;
                break;
            case "DO":
                read = () => io.TryReadDo(index, out bool on) ? on ? 1 : 0 : null;
                break;
            case "AI":
                read = () => io.TryReadAi(index, out double value) ? value : null;
                break;
            default:
                read = () => io.TryReadAo(index, out double value) ? value : null;
                break;
        }

        string description = point is null ? string.Empty : $"{point.Name} {point.Description}".Trim();
        return new DataSignal(name, type is "DI" or "DO", type, $"{type}{index}", point?.Unit ?? string.Empty, description, read);
    }

    /// <summary>
    /// 到了下一个整周期就采一行（1 秒周期就是整秒），行的时间记那个整周期
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        if (!_bound)
        {
            return;
        }

        long interval = Math.Max(100, SampleIntervalMs);
        long now = NowMs();
        if (_next == 0 || _next - now > interval || now - _next >= interval)
        {
            // 头一拍，或者时钟调过、扫描卡了一个周期以上：从下一个整周期重新对齐，中间的不补
            _next = AlignUp(now, interval);
            return;
        }

        if (now < _next)
        {
            return;
        }

        long time = _next;
        _next += interval;
        SampleRow(time);
    }

    /// <summary>
    /// 采一行发出去；退出时停了就不采
    /// </summary>
    private void SampleRow(long time)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _rowDone.Reset();
        }

        try
        {
            var values = new double?[_signals.Count];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = _signals[index].Read();
            }

            _records.OnNext(new DataRecord(time, values));
            _lastFault = null;
        }
        catch (Exception exception)
        {
            // 订阅方抛出来的也兜住，同样的错只记一次
            if (_lastFault != exception.Message)
            {
                _lastFault = exception.Message;
                LogHelper.Warn(Name, $"数据曲线采样出错: {exception.Message}");
            }
        }
        finally
        {
            _rowDone.Set();
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
    /// 写库管道
    /// </summary>
    private void StartWriter()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _writerDone = done;
        var writerThread = new EventLoopScheduler(start => new Thread(start) { Name = "DataChart.Writer", IsBackground = true });

        _records
            .Buffer(() => Observable.Timer(TimeSpan.FromSeconds(Math.Clamp(RecordBatchSeconds, 1, 60))))
            .Where(batch => batch.Count > 0)
            .Do(CountPending) //等待推送
            .ObserveOn(writerThread)
            .Subscribe(WriteBatch,
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

        // WAL 是 SQLite 自己的设置，SqlSugar 没有对应的接口；换了别的库就不用切
        if (db.CurrentConnectionConfig.DbType == SqlSugar.DbType.Sqlite)
        {
            db.Ado.ExecuteCommand("PRAGMA journal_mode=WAL");
        }

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

    #region 过期清理

    private DateTime _cleanedDay = DateTime.MinValue;

    /// <summary>
    /// 按天分表 过期删除
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
                db.DbMaintenance.DropTable(table.Value);
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
    /// 按名字查 [start, end)（本机时间）的曲线
    /// </summary>
    /// <param name="names"></param>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="token"></param>
    /// <returns></returns>
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

    /// <summary>
    /// 宿主退出：停采样，把攒着的最后一批写进库
    /// </summary>
    public override void OnStopping()
    {
        StopSampling();
    }
}
