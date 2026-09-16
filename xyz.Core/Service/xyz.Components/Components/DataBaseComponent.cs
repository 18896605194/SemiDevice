using SqlSugar;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Configs.Models;
using xyz.Database.DbProvider;

namespace xyz.Components.Components;

/// <summary>
/// 数据库组件：一个节点就是一个具名连接，节点名即库名（Default / Wafer / Log ……）。
/// 装配时把自己注册进 XyzDb，之后任何地方都能用 XyzDb.Create("库名") 拿到对应连接。
/// 按用途分库：流水这种高频写不要跟登录鉴权挤同一个 SQLite 文件，互相抢写锁，坏一个也不牵连其它。
/// </summary>
[Component(description: "数据库连接（节点名即库名）")]
public class DataBaseComponent : ComponentBase
{
    #region SC 装机常量

    /// <summary>
    /// 数据库连接字符串，通过 sc.xml 的 Value 注入。
    /// </summary>
    [SCEditor(XyzDb.DefaultConnectionString, "Database", "数据库连接字符串")]
    public string ConnectionString { get; set; } = XyzDb.DefaultConnectionString;

    /// <summary>
    /// 数据库类型，通过 sc.xml 的 Value 注入。
    /// </summary>
    [SCEditor("Sqlite", "Database", "数据库类型")]
    public DbType DbType { get; set; } = DbType.Sqlite;

    #endregion

    #region 数据库访问

    /// <summary>
    /// 装配读完 SC 后，按本节点名把连接注册进 XyzDb。
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        XyzDb.Register(Name, ConnectionString, DbType);
    }

    /// <summary>
    /// 根据当前连接字符串和数据库类型创建 SqlSugar 数据库客户端。
    /// </summary>
    public SqlSugarClient CreateDb()
    {
        return XyzDb.CreateFor(ConnectionString, DbType);
    }

    #endregion
}
