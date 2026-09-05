using SqlSugar;

namespace xyz.Database.DbProvider;

/// <summary>
/// SqlSugar 数据库访问入口。
/// </summary>
public static class XyzDb
{
    /// <summary>
    /// SQLite 数据库文件路径，后续可改成从配置读取。
    /// </summary>
    public const string DefaultConnectionString = "DataSource=xyz.db";

    /// <summary>
    /// 创建一个 SqlSugarClient。
    /// </summary>
    public static SqlSugarClient Create(
        string connectionString = DefaultConnectionString,
        DbType dbType = DbType.Sqlite)
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = dbType,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
    }

    /// <summary>
    /// 根据实体自动创建表。
    /// </summary>
    public static void InitTables<T>() where T : class, new()
    {
        using var db = Create();
        db.CodeFirst.InitTables<T>();
    }
}
