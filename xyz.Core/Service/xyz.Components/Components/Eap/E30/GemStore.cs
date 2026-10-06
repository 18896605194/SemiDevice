using SqlSugar;
using xyz.Common.Log;
using xyz.Database.DbProvider;
using xyz.Database.Eap;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// GEM 存盘：设定一行 JSON（gem_config），缓存的报文一条一行（gem_spool）。
/// 量很小（设定只在 Host 改的时候写，缓存只在断了通讯时写），直接同步写；写不进去只记日志，GEM 照跑。
/// 几条线程都会写（派发线程改设定、发送线程进出缓存），所以写库串成一条。
/// </summary>
internal sealed class GemStore
{
    private readonly string _database;
    private readonly Func<string> _owner;
    private readonly object _gate = new();
    private bool _tablesReady;
    private bool _failing;

    public GemStore(string database, Func<string> owner)
    {
        _database = database;
        _owner = owner;
    }

    /// <summary>读回存着的设定；没存过或读不出来返回 null（当第一次开机，记日志）。</summary>
    public GemConfig? LoadConfig()
    {
        return Run("读 GEM 设定", db =>
        {
            var row = db.Queryable<GemConfigEntity>().InSingle(1);
            return row is null || string.IsNullOrWhiteSpace(row.Json) ? null : JsonHelper.Deserialize<GemConfig>(row.Json);
        });
    }

    /// <summary>存一份设定（JSON 由调用方在锁里转好）。</summary>
    public void SaveConfig(string json)
    {
        Run("存 GEM 设定", db =>
        {
            db.Storageable(new GemConfigEntity { Id = 1, Json = json, SavedAt = DateTime.Now }).ExecuteCommand();
            return true;
        });
    }

    /// <summary>缓存里现在有几条。</summary>
    public int CountSpool()
    {
        return Run("数缓存", db => db.Queryable<GemSpoolEntity>().Count());
    }

    /// <summary>进缓存一条。</summary>
    public bool AppendSpool(GemSpoolEntity row)
    {
        return Run("写缓存", db => db.Insertable(row).ExecuteCommand() > 0);
    }

    /// <summary>最早的一条（先进先出）；空了返回 null。</summary>
    public GemSpoolEntity? OldestSpool()
    {
        return Run("取缓存", db => db.Queryable<GemSpoolEntity>().OrderBy(row => row.Id).First());
    }

    /// <summary>删一条（发出去了、或者满了被挤掉）。</summary>
    public void RemoveSpool(long id)
    {
        Run("删缓存", db => db.Deleteable<GemSpoolEntity>().In(id).ExecuteCommand());
    }

    /// <summary>清空缓存（Host 让清掉）。</summary>
    public void ClearSpool()
    {
        Run("清缓存", db => db.Deleteable<GemSpoolEntity>().ExecuteCommand());
    }

    /// <summary>串行地跑一次库操作：表第一次用时建；出错记一次日志（连着出错不刷屏），返回默认值。</summary>
    private T? Run<T>(string what, Func<ISqlSugarClient, T> action)
    {
        lock (_gate)
        {
            try
            {
                using var db = XyzDb.Create(_database);
                if (!_tablesReady)
                {
                    db.CodeFirst.InitTables<GemConfigEntity, GemSpoolEntity>();
                    _tablesReady = true;
                }

                var result = action(db);
                if (_failing)
                {
                    _failing = false;
                    LogHelper.Info(_owner(), "GEM 存盘恢复正常");
                }

                return result;
            }
            catch (Exception exception)
            {
                if (!_failing)
                {
                    _failing = true;
                    LogHelper.Warn(_owner(), $"{what}失败（GEM 照跑）：{exception.Message}");
                }

                return default;
            }
        }
    }
}
