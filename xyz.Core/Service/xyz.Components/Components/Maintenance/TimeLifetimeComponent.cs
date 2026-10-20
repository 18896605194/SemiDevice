using System.Threading.Channels;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Database.DbProvider;
using xyz.Database.Lifetime;

namespace xyz.Components.Components;

/// <summary>
/// 寿命（时间）
/// </summary>
[Component(description: "部件寿命（时间）组件（从上次清零起按实际时间算，关机也照算；到 EC 警告时间报警告、到报警时间报报警）")]
public class TimeLifetimeComponent : ComponentBase, ITimeLifetime
{
    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "min", "0", "2147483647", "0",
        "警告时间：从上次清零起到了这么多分钟报寿命快到期警告；0 = 不管")]
    public int WarnMinutes
    {
        get { return GetEcInt(nameof(WarnMinutes)); }
        set { SetEcInt(nameof(WarnMinutes), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "min", "0", "2147483647", "0",
        "报警时间：从上次清零起到了这么多分钟报寿命到期报警；0 = 不管")]
    public int AlarmMinutes
    {
        get { return GetEcInt(nameof(AlarmMinutes)); }
        set { SetEcInt(nameof(AlarmMinutes), value); }
    }

    #endregion

    #region Alarm

    [Alarm("寿命快到期", AlarmCategory.Other, AlarmLevel = AlarmLevel.Warn,
        Description = "从上次清零起的时间到了 EC 设的警告时间",
        Solution = "准备换件（换液）；换完把已用时间清零再复位报警")]
    public string WarnAlarm = nameof(WarnAlarm);

    [Alarm("寿命到期", AlarmCategory.Other, AlarmLevel = AlarmLevel.Alarm1,
        Description = "从上次清零起的时间到了 EC 设的报警时间",
        Solution = "换件（换液），换完把已用时间清零再复位报警；暂时不换就把 EC AlarmMinutes 调高")]
    public string ExpiredAlarm = nameof(ExpiredAlarm);

    #endregion

    #region 时间

    private readonly object _gate = new();

    /// <summary>
    /// 从这一刻起算（上次清零的时间）
    /// </summary>
    private DateTime _startedAt;

    /// <summary>
    /// 开机从库里读过了（只读一次）；读过以后才往库里写
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// 库里这一行的键（开机读库时取的全路径）
    /// </summary>
    private string _path = string.Empty;

    /// <summary>
    /// 后台写库的队列：放从哪一刻起算，一个读者按先后写
    /// </summary>
    private readonly Channel<DateTime> _writes =
        Channel.CreateUnbounded<DateTime>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>
    /// 已用时间：开机读库之前是 0；时钟往回调到起算之前也是 0
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
        var before = UsedTime;
        var now = ReadNow();
        bool loaded;
        lock (_gate)
        {
            _startedAt = now;
            loaded = _loaded;
        }

        if (loaded)
        {
            _writes.Writer.TryWrite(now);
        }

        LogHelper.Info(FullPath, $"已用时间清零（换件），清零前 {(long)before.TotalMinutes} 分钟");
    }

    #endregion

    public override void Bind()
    {
        Load();
    }

    #region 扫描

    /// <summary>
    /// 每拍看一次已用时间：到了报警时间报报警，没到报警时间、到了警告时间报警告
    /// （报过的再报不重复，人工复位后还到着就再报）
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
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
    /// 现在几点；探针测试重写它直接摆时间
    /// </summary>
    protected virtual DateTime ReadNow()
    {
        return DateTime.Now;
    }

    #endregion

    #region 存库

    /// <summary>
    /// 开机读回从哪一刻起算；库里没有（第一次开机）就从现在算并存下来；读不了只记日志，这次从现在算、库里不动
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
                _writes.Writer.TryWrite(now);
            }
            else
            {
                lock (_gate)
                {
                    _startedAt = row.StartedAt;
                }
            }
        }
        catch (Exception exception)
        {
            LogHelper.Error(path, $"读起算时间失败，这次从现在算（库里不动）：{exception.Message}");
        }

        _ = Task.Run(WriteLoopAsync);
    }

    /// <summary>
    /// 后台写库：把那行的起算时间改成新的，没有这一行就插一行；写不进去只记日志，不影响设备
    /// </summary>
    private async Task WriteLoopAsync()
    {
        await foreach (var startedAt in _writes.Reader.ReadAllAsync())
        {
            try
            {
                using var db = XyzDb.Create();
                int updated = db.Updateable<TimeLifetimeEntity>()
                    .SetColumns(item => new TimeLifetimeEntity { StartedAt = startedAt })
                    .Where(item => item.Path == _path)
                    .ExecuteCommand();
                if (updated == 0)
                {
                    db.Insertable(new TimeLifetimeEntity { Path = _path, StartedAt = startedAt }).ExecuteCommand();
                }
            }
            catch (Exception exception)
            {
                LogHelper.Error(_path, $"写起算时间失败：{exception.Message}");
            }
        }
    }

    #endregion
}
