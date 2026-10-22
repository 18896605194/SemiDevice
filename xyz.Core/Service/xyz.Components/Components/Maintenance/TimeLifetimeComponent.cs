using System.Threading.Channels;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Database.DbProvider;
using xyz.Database.Lifetime;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// 寿命（时间）
/// </summary>
[Component(description: "部件寿命（时间）组件（默认从上次清零起按实际时间算，关机也照算；配了 DoIndex 只算那个 DO 通着的时间；配了 ClearDoIndex 那个 DO 每变一次就清零重算；到 EC 警告时间报警告、到报警时间报报警）")]
public class TimeLifetimeComponent : ComponentBase, ITimeLifetime
{
    #region SC

    [SCEditor("-1", "IO", "计时 DO 索引（真 DO，或虚拟 IO 里的虚拟 DO 5000~5999）：只算它通着的时间，泵、加热器的运行时长填它的 DO；-1 = 不盯，从上次清零起按实际时间算（关机也算）")]
    public int DoIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "清零 DO 索引（真 DO，或虚拟 IO 里的虚拟 DO 5000~5999）：每变一次（断→通、通→断）清零重算，换液、换件这类由程序把虚拟 DO 翻一下；-1 = 只人工清零")]
    public int ClearDoIndex { get; set; } = -1;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "min", "0", "2147483647", "0",
        "警告时间：已用时间到了这么多分钟报寿命快到期警告；0 = 不管")]
    public int WarnMinutes
    {
        get { return GetEcInt(nameof(WarnMinutes)); }
        set { SetEcInt(nameof(WarnMinutes), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "min", "0", "2147483647", "0",
        "报警时间：已用时间到了这么多分钟报寿命到期报警；0 = 不管")]
    public int AlarmMinutes
    {
        get { return GetEcInt(nameof(AlarmMinutes)); }
        set { SetEcInt(nameof(AlarmMinutes), value); }
    }

    #endregion

    #region Alarm

    [Alarm("寿命快到期", AlarmCategory.EquipmentStatusWarning, AlarmLevel = AlarmLevel.Warn,
        Description = "已用时间（从上次清零起；配了计时 DO 的只算它通着的时间）到了 EC 设的警告时间",
        Solution = "准备换件（换液）；换完把已用时间清零（配了清零 DO 的会自动清）再复位报警")]
    public string WarnAlarm = nameof(WarnAlarm);

    [Alarm("寿命到期", AlarmCategory.EquipmentStatusWarning, AlarmLevel = AlarmLevel.Alarm1,
        Description = "已用时间（从上次清零起；配了计时 DO 的只算它通着的时间）到了 EC 设的报警时间",
        Solution = "换件（换液），换完把已用时间清零（配了清零 DO 的会自动清）再复位报警；暂时不换就把 EC AlarmMinutes 调高")]
    public string ExpiredAlarm = nameof(ExpiredAlarm);

    #endregion

    #region 时间

    /// <summary>
    /// 计时 DO 通着时攒满这么多秒写一次库（断电最多丢这么久）
    /// </summary>
    private const int SaveEverySeconds = 60;

    private readonly object _gate = new();

    /// <summary>
    /// 上次清零的时间（不盯 DO 的从这一刻起算）
    /// </summary>
    private DateTime _startedAt;

    /// <summary>
    /// 盯 DO 的：DO 通着累计的时长（含开机从库里读回的）
    /// </summary>
    private TimeSpan _runTime;

    /// <summary>
    /// 盯 DO 的：已经算进累计、还没写进库的时长
    /// </summary>
    private TimeSpan _unsaved;

    /// <summary>
    /// 开机从库里读过了（只读一次）；读过以后才往库里写
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// 库里这一行的键（开机读库时取的全路径）
    /// </summary>
    private string _path = string.Empty;

    /// <summary>
    /// 后台写库的队列：一个读者按先后写
    /// </summary>
    private readonly Channel<TimeLifetimeWrite> _writes =
        Channel.CreateUnbounded<TimeLifetimeWrite>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>
    /// 已用时间：不盯 DO 是从上次清零到现在（时钟往回调到起算之前算 0），盯 DO 是 DO 通着累计的；开机读库之前是 0
    /// </summary>
    public TimeSpan UsedTime
    {
        get
        {
            DateTime startedAt;
            lock (_gate)
            {
                if (!_loaded)
                {
                    return TimeSpan.Zero;
                }

                if (DoIndex >= 0)
                {
                    return _runTime;
                }

                startedAt = _startedAt;
            }

            var used = ReadNow() - startedAt;
            if (used < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return used;
        }
    }

    /// <summary>
    /// 换件以后清零：从现在起重新算，库里那行交给后台改
    /// </summary>
    public void Clear()
    {
        Restart("换件");
    }

    /// <summary>
    /// 清零重算：起算时间改成现在、通着累计的归零，库里那行交给后台改
    /// </summary>
    private void Restart(string reason)
    {
        var before = UsedTime;
        var now = ReadNow();
        lock (_gate)
        {
            _startedAt = now;
            _runTime = TimeSpan.Zero;
            _unsaved = TimeSpan.Zero;
            if (_loaded)
            {
                // 在锁里排队：跟扫描线程排的"加几秒"前后不乱
                _writes.Writer.TryWrite(new TimeLifetimeWrite(true, now, 0));
            }
        }

        LogHelper.Info(FullPath, $"已用时间清零（{reason}），清零前 {(long)before.TotalMinutes} 分钟");
    }

    #endregion

    private IIo? _io;

    private IVirtualIo? _virtualIo;

    /// <summary>
    /// SC 灌完值：计时 DO 和清零 DO 配成同一个，通断一次就清零一次、算不出时长，开机就报
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        if (DoIndex >= 0 && DoIndex == ClearDoIndex)
        {
            throw new InvalidOperationException($"{FullPath} 的 DoIndex 和 ClearDoIndex 配成了同一个 DO{DoIndex}：通断一次就清零一次，算不出时长");
        }
    }

    public override void Bind()
    {
        _io = IocHelper.GetService<IIo>();
        _virtualIo = IocHelper.GetService<IVirtualIo>();
        Load();
    }

    #region 扫描

    /// <summary>
    /// 上一拍读到的清零 DO；读不到是 null，重新读到的那一拍只记下不算
    /// </summary>
    private bool? _lastClearLevel;

    /// <summary>
    /// 上一拍读到的计时 DO；读不到是 null，重新读到的那一拍只记下不算
    /// </summary>
    private bool? _lastRunLevel;

    /// <summary>
    /// 上一拍的开机计时读数
    /// </summary>
    private TimeSpan _lastUptime;

    /// <summary>
    /// 每拍先看清零 DO、再给计时 DO 累计，最后按已用时间报警告或报警（报过的不重复报，人工复位后还到着就再报）
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        ScanClearDo();
        ScanRunDo();

        // 到了报警时间只报报警，不再叠一条警告
        double minutes = UsedTime.TotalMinutes;
        int alarmMinutes = AlarmMinutes;
        if (alarmMinutes > 0 && minutes >= alarmMinutes)
        {
            RaiseAlarm(ExpiredAlarm);
            return;
        }

        int warnMinutes = WarnMinutes;
        if (warnMinutes > 0 && minutes >= warnMinutes)
        {
            RaiseAlarm(WarnAlarm);
        }
    }

    /// <summary>
    /// 清零 DO 每变一次（断→通、通→断）就清零重算；第一拍、读不到以后重新读到的那一拍只记下
    /// </summary>
    private void ScanClearDo()
    {
        if (ClearDoIndex < 0)
        {
            return;
        }

        bool? level = ReadDo(ClearDoIndex);
        if (level is not null && _lastClearLevel is not null && level.Value != _lastClearLevel.Value)
        {
            Restart($"DO{ClearDoIndex} 变了");
        }

        _lastClearLevel = level;
    }

    /// <summary>
    /// 计时 DO 上一拍通着、这一拍读得到，就把两拍之间的时长算进去；攒的时长按整秒写库
    /// </summary>
    private void ScanRunDo()
    {
        if (DoIndex < 0)
        {
            return;
        }

        var uptime = ReadUptime();
        bool? level = ReadDo(DoIndex);
        var elapsed = uptime - _lastUptime;
        bool counted = level is not null && _lastRunLevel == true && elapsed > TimeSpan.Zero;
        _lastRunLevel = level;
        _lastUptime = uptime;
        lock (_gate)
        {
            if (counted)
            {
                _runTime += elapsed;
                _unsaved += elapsed;
            }

            // 通着时攒满了写一次；不通了（断了、读不到）把攒下的整秒写掉；零头留着下次一起写
            bool full = _unsaved >= TimeSpan.FromSeconds(SaveEverySeconds);
            bool stopped = level != true && _unsaved >= TimeSpan.FromSeconds(1);
            if (!_loaded || (!full && !stopped))
            {
                return;
            }

            long seconds = (long)_unsaved.TotalSeconds;
            _unsaved -= TimeSpan.FromSeconds(seconds);

            // 在锁里排队：跟清零排的写前后不乱
            _writes.Writer.TryWrite(new TimeLifetimeWrite(false, _startedAt, seconds));
        }
    }

    /// <summary>
    /// 读 DO（true = 通）：虚拟 IO 里有这个点号就读它，没有再读真 DO；都读不到返回 null
    /// </summary>
    private bool? ReadDo(int index)
    {
        bool on;
        var virtualIo = _virtualIo;
        if (virtualIo is not null && virtualIo.TryReadDo(index, out on))
        {
            return on;
        }

        var io = _io;
        if (io is null || !io.TryReadDo(index, out on))
        {
            return null;
        }

        return on;
    }

    /// <summary>
    /// 现在几点；探针测试重写它直接摆时间
    /// </summary>
    protected virtual DateTime ReadNow()
    {
        return DateTime.Now;
    }

    /// <summary>
    /// 开机以来过了多久（只往前走，改系统时钟不影响）；探针测试重写它直接摆时间
    /// </summary>
    protected virtual TimeSpan ReadUptime()
    {
        return TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    #endregion

    #region 存库

    /// <summary>
    /// 开机读回起算时间和通着的秒数；第一次开机库里没有就从现在算并存下来
    /// </summary>
    private void Load()
    {
        var now = ReadNow();
        string path;
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _path = FullPath;
            _startedAt = now;
            path = _path;
        }

        try
        {
            using var db = XyzDb.Create();
            db.CodeFirst.InitTables<TimeLifetimeEntity>();
            var row = db.Queryable<TimeLifetimeEntity>().First(item => item.Path == path);
            if (row is null)
            {
                _writes.Writer.TryWrite(new TimeLifetimeWrite(true, now, 0));
            }
            else
            {
                lock (_gate)
                {
                    _startedAt = row.StartedAt;
                    _runTime += TimeSpan.FromSeconds(row.UsedSeconds);
                }
            }
        }
        catch (Exception exception)
        {
            // 读不成只记日志：盯 DO 的这次从 0 算（库里照样只加不写总数），不盯的这次从现在算（库里不动）
            if (DoIndex >= 0)
            {
                LogHelper.Error(path, $"读已用时间失败，这次从 0 算（库里照样加）：{exception.Message}");
            }
            else
            {
                LogHelper.Error(path, $"读起算时间失败，这次从现在算（库里不动）：{exception.Message}");
            }
        }

        _ = Task.Run(WriteLoopAsync);
    }

    /// <summary>
    /// 后台写库：清零改起算时间、秒数归 0，加时长只给那行加几秒，没有这一行就插一行；写不进去只记日志
    /// </summary>
    private async Task WriteLoopAsync()
    {
        await foreach (var write in _writes.Reader.ReadAllAsync())
        {
            try
            {
                using var db = XyzDb.Create();
                int updated;
                if (write.Clear)
                {
                    updated = db.Updateable<TimeLifetimeEntity>()
                        .SetColumns(item => new TimeLifetimeEntity { StartedAt = write.StartedAt, UsedSeconds = 0 })
                        .Where(item => item.Path == _path)
                        .ExecuteCommand();
                }
                else
                {
                    updated = db.Updateable<TimeLifetimeEntity>()
                        .SetColumns(item => new TimeLifetimeEntity { UsedSeconds = item.UsedSeconds + write.AddSeconds })
                        .Where(item => item.Path == _path)
                        .ExecuteCommand();
                }

                if (updated == 0)
                {
                    db.Insertable(new TimeLifetimeEntity { Path = _path, StartedAt = write.StartedAt, UsedSeconds = write.AddSeconds }).ExecuteCommand();
                }
            }
            catch (Exception exception)
            {
                LogHelper.Error(_path, $"写已用时间失败：{exception.Message}");
            }
        }
    }

    #endregion
}
