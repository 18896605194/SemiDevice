using System.Collections.Concurrent;
using SqlSugar;

namespace xyz.Database.DbProvider;

/// <summary>
/// SqlSugar 数据库访问入口：按名字分库。
/// 每个用途一个连接（登录鉴权、晶圆流水、日志……），互不影响——流水写得又多又频，
/// 跟鉴权挤在同一个 SQLite 文件上会互相抢写锁，一个库出问题也不该把别的拖下水。
/// 连接在装配时由 sc.xml 的 Database 节点注册（见 DataBaseComponent）；没注册的名字回落到默认连接。
/// </summary>
public static class XyzDb
{
    /// <summary>
    /// 默认连接名：没指定库名时用它（登录鉴权这类基础表）。
    /// </summary>
    public const string DefaultName = "Default";

    /// <summary>
    /// 默认 SQLite 数据库文件。
    /// </summary>
    public const string DefaultConnectionString = "DataSource=xyz.db";

    private static readonly ConcurrentDictionary<string, (string ConnectionString, DbType DbType)> Connections =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册一个具名连接（重复注册同名会覆盖，装配时调）。
    /// </summary>
    public static void Register(string name, string connectionString, DbType dbType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        Connections[name] = (connectionString, dbType);
    }

    /// <summary>
    /// 这个名字注册过没有。
    /// </summary>
    public static bool IsRegistered(string name)
    {
        return Connections.ContainsKey(name);
    }

    /// <summary>
    /// 已注册的连接名。
    /// </summary>
    public static IReadOnlyCollection<string> RegisteredNames => Connections.Keys.ToArray();

    /// <summary>
    /// 按名字建客户端；没注册过的名字回落到默认连接（装机漏配时仍能跑，只是都挤在默认库里）。
    /// </summary>
    public static SqlSugarClient Create(string name = DefaultName)
    {
        var connection = Connections.TryGetValue(name, out var registered)
            ? registered
            : (DefaultConnectionString, DbType.Sqlite);

        return CreateFor(connection.Item1, connection.Item2);
    }

    /// <summary>
    /// 用明确的连接串建客户端（不走注册表）。
    /// </summary>
    public static SqlSugarClient CreateFor(string connectionString, DbType dbType = DbType.Sqlite)
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = dbType,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });
    }

    /// <summary>
    /// 在指定库里按实体建表。
    /// </summary>
    public static void InitTables<T>(string name = DefaultName) where T : class, new()
    {
        using var db = Create(name);
        db.CodeFirst.InitTables<T>();
    }
}
