using System.Threading.Channels;
using SqlSugar;
using xyz.Common.Log;
using xyz.Database.DbProvider;
using xyz.Database.Jobs;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// Job 存盘：每次发布的全貌交给一条写库线程，只写最新的一份（来不及写的旧版本直接跳过）；开机读回上一份。
/// 写库不在扫描线程上做——磁盘一卡，扫描就卡。写不进去只记日志，Job 照跑。
/// </summary>
internal sealed class JobStore
{
    private readonly string _database;
    private readonly Func<string> _owner;
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private JobListDto? _pending;
    private bool _tableReady;
    private bool _failing;

    public JobStore(string database, Func<string> owner)
    {
        _database = database;
        _owner = owner;
        _ = Task.Run(WriteLoopAsync);
    }

    /// <summary>读回上一份全貌；没存过返回 null，读不出来也返回 null（记日志，当没有上次的 Job）。</summary>
    public JobListDto? Load()
    {
        try
        {
            using var db = XyzDb.Create(_database);
            EnsureTable(db);
            var row = db.Queryable<JobSnapshotEntity>().InSingle(1);
            if (row is null || string.IsNullOrWhiteSpace(row.Json))
            {
                return null;
            }

            return JsonHelper.Deserialize<JobListDto>(row.Json);
        }
        catch (Exception exception)
        {
            LogHelper.Error(_owner(), $"读上次的 Job 存盘失败（当没有上次的 Job）：{exception.Message}");
            return null;
        }
    }

    /// <summary>交一份全貌去存（任意线程）：只留最新的，写库线程空了就写。</summary>
    public void Save(JobListDto snapshot)
    {
        Volatile.Write(ref _pending, snapshot);
        _signal.Writer.TryWrite(true);
    }

    private async Task WriteLoopAsync()
    {
        while (await _signal.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            _signal.Reader.TryRead(out _);
            var snapshot = Interlocked.Exchange(ref _pending, null);
            if (snapshot is not null)
            {
                Write(snapshot);
            }
        }
    }

    private void Write(JobListDto snapshot)
    {
        try
        {
            using var db = XyzDb.Create(_database);
            EnsureTable(db);
            var row = new JobSnapshotEntity
            {
                Id = 1,
                Version = snapshot.Version,
                Json = JsonHelper.Serialize(snapshot),
                SavedAt = DateTime.Now,
            };
            db.Storageable(row).ExecuteCommand();
        }
        catch (Exception exception)
        {
            if (!_failing)
            {
                _failing = true;
                LogHelper.Warn(_owner(), $"Job 存盘失败（Job 照跑，下一次变化再存）：{exception.Message}");
            }

            return;
        }

        if (_failing)
        {
            _failing = false;
            LogHelper.Info(_owner(), "Job 存盘恢复正常");
        }
    }

    /// <summary>表第一次用到时建（已有就只补列）。</summary>
    private void EnsureTable(ISqlSugarClient db)
    {
        if (_tableReady)
        {
            return;
        }

        db.CodeFirst.InitTables<JobSnapshotEntity>();
        _tableReady = true;
    }
}
