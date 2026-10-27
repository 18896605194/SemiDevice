using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using xyz.Common.Log;
using xyz.Database.DbProvider;
using xyz.Database.Process;

namespace xyz.Components.Components;

/// <summary>
/// 工艺站点的开工计数：今天几片、累计几片、最近一次开工的时间戳；按天一站一行存库，开机读回来接着数
/// </summary>
public sealed class ProcessCounter
{
    private readonly object _gate = new();

    /// <summary>
    /// 后台写库的队列：开工一次放一行（哪一天、加几片），一个读者按先后写
    /// </summary>
    private readonly Channel<ProcessDailyCountEntity> _writes =
        Channel.CreateUnbounded<ProcessDailyCountEntity>(new UnboundedChannelOptions { SingleReader = true });

    private string _station = string.Empty;

    /// <summary>
    /// 日志记在谁名下（这个站点模块）；Load 之前为 null
    /// </summary>
    private ILogSource? _owner;
    private string _database = XyzDb.DefaultName;

    /// <summary>
    /// 开机读过库了（只读一次）；读过以后开工才往库里写
    /// </summary>
    private bool _loaded;

    private DateTime _day = DateTime.Today;
    private int _today;
    private long _total;

    /// <summary>
    /// 最近一次开工的时间戳：用 Stopwatch 开机以来的计时，不用系统时间——Host 对时、人改时间往回调，先后也不会乱
    /// </summary>
    private long? _lastStartedTimestamp;

    /// <summary>
    /// 今天开工了几片；过了零点还没开工就是 0
    /// </summary>
    public int Today
    {
        get
        {
            lock (_gate)
            {
                if (_day != DateTime.Today)
                {
                    return 0;
                }

                return _today;
            }
        }
    }

    /// <summary>
    /// 累计开工了几片
    /// </summary>
    public long Total
    {
        get
        {
            lock (_gate)
            {
                return _total;
            }
        }
    }

    /// <summary>
    /// 最近一次开工的时间戳（Stopwatch 计时）；这次开机还没开过工为空
    /// </summary>
    public long? LastStartedTimestamp
    {
        get
        {
            lock (_gate)
            {
                return _lastStartedTimestamp;
            }
        }
    }

    /// <summary>
    /// 开机读回来：这个站今天那行接着数，各天加起来是累计；读不了只记日志（记在 owner 名下），今天、累计这次从 0 数（库里照样按天加）
    /// </summary>
    public void Load(string station, string database, ILogSource owner)
    {
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            _station = station;
            _owner = owner;
            _database = database;
            _loaded = true;
        }

        try
        {
            using var db = XyzDb.Create(database);
            db.CodeFirst.InitTables<ProcessDailyCountEntity>();
            var rows = db.Queryable<ProcessDailyCountEntity>().Where(row => row.Station == station).ToList();
            lock (_gate)
            {
                string today = DayText(_day);
                _today += rows.Where(row => row.Day == today).Sum(row => row.Wafers);
                _total += rows.Sum(row => (long)row.Wafers);
            }
        }
        catch (Exception exception)
        {
            LogHelper.Error(owner, $"读开工片数失败，今天、累计这次从 0 数（库里照样按天加）：{exception.Message}");
        }

        _ = Task.Run(WriteLoopAsync);
    }

    /// <summary>
    /// 开工一片：今天、累计各加 1，记下开工时间戳，交给后台给库里这一天加 1（站点在自己的锁里调，这里不碰库）
    /// </summary>
    public void Started()
    {
        var now = DateTime.Now;
        ProcessDailyCountEntity? row = null;
        lock (_gate)
        {
            if (_day != now.Date)
            {
                _day = now.Date;
                _today = 0;
            }

            _today++;
            _total++;
            _lastStartedTimestamp = Stopwatch.GetTimestamp();
            if (_loaded)
            {
                row = new ProcessDailyCountEntity { Station = _station, Day = DayText(now.Date), Wafers = 1, UpdatedAt = now };
            }
        }

        if (row is not null)
        {
            _writes.Writer.TryWrite(row);
        }
    }

    /// <summary>
    /// 后台写库：给这一天那行加上片数，没有这一行就插一行。只加不写总数：时钟往回调到前一天，也只是给那天加 1，不会把那天的数冲掉。
    /// 写不进去只记日志，不影响设备
    /// </summary>
    private async Task WriteLoopAsync()
    {
        await foreach (var row in _writes.Reader.ReadAllAsync())
        {
            try
            {
                using var db = XyzDb.Create(_database);
                int added = row.Wafers;
                int updated = db.Updateable<ProcessDailyCountEntity>()
                    .SetColumns(item => new ProcessDailyCountEntity { Wafers = item.Wafers + added, UpdatedAt = row.UpdatedAt })
                    .Where(item => item.Station == row.Station && item.Day == row.Day)
                    .ExecuteCommand();
                if (updated == 0)
                {
                    db.Insertable(row).ExecuteCommand();
                }
            }
            catch (Exception exception)
            {
                var owner = _owner;
                if (owner is null)
                {
                    LogHelper.Error(row.Station, $"写开工片数失败：{exception.Message}");
                }
                else
                {
                    LogHelper.Error(owner, $"写开工片数失败：{exception.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 库里按天存的写法
    /// </summary>
    private static string DayText(DateTime day)
    {
        return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
