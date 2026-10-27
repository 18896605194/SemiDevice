using xyz.Common.Log;
using xyz.Database.DbProvider;
using xyz.Database.Eda;
using xyz.Secs.Eda;
using xyz.Tools;

namespace xyz.Elements.Elements;

/// <summary>
/// EDA 存盘：eda_config 表一块一行 JSON（行号就是 EDA 给的块号：权限表 1、持久计划 2）。量很小，只在客户端改权限、
/// 定义 / 删除持久计划时写，直接同步写；写不进去只记日志，EDA 照跑。写库串成一条
/// </summary>
internal sealed class EdaStore : IEdaStore
{
    private readonly string _database;
    private readonly ILogSource _owner;
    private readonly object _gate = new();
    private bool _tableReady;
    private bool _failing;

    public EdaStore(string database, ILogSource owner)
    {
        _database = database;
        _owner = owner;
    }

    /// <summary>
    /// 读回一块；没存过或读不出来返回默认值（当第一次开机，读不出来记日志）
    /// </summary>
    public T? Load<T>(int id)
    {
        return Run("读 EDA 设定", db =>
        {
            var entity = db.Queryable<EdaConfigEntity>().InSingle(id);
            if (entity is null || string.IsNullOrWhiteSpace(entity.Json))
            {
                return default;
            }

            return JsonHelper.Deserialize<T>(entity.Json);
        });
    }

    /// <summary>
    /// 存一块（整块覆盖）
    /// </summary>
    public void Save<T>(int id, T value)
    {
        string json = JsonHelper.Serialize(value);
        Run("存 EDA 设定", db =>
        {
            db.Storageable(new EdaConfigEntity { Id = id, Json = json, SavedAt = DateTime.Now }).ExecuteCommand();
            return true;
        });
    }

    /// <summary>
    /// 串行地跑一次库操作：表第一次用时建；出错只记一次日志（连着出错不刷屏），返回默认值
    /// </summary>
    private T? Run<T>(string what, Func<SqlSugar.ISqlSugarClient, T> action)
    {
        lock (_gate)
        {
            try
            {
                using var db = XyzDb.Create(_database);
                if (!_tableReady)
                {
                    db.CodeFirst.InitTables<EdaConfigEntity>();
                    _tableReady = true;
                }

                var result = action(db);
                if (_failing)
                {
                    _failing = false;
                    LogHelper.Info(_owner, "EDA 存盘恢复正常");
                }

                return result;
            }
            catch (Exception exception)
            {
                if (!_failing)
                {
                    _failing = true;
                    LogHelper.Warn(_owner, $"{what}失败（EDA 照跑）：{exception.Message}");
                }

                return default;
            }
        }
    }
}
