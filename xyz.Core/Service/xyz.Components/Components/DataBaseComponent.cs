using SqlSugar;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Configs.Models;
using xyz.Database.DbProvider;

namespace xyz.Components.Components;

[Component(description: "数据库连接（节点名即库名）")]
public class DataBaseComponent : ComponentBase
{
    #region SC 

    [SCEditor(XyzDb.DefaultConnectionString, "Database", "数据库连接字符串")]
    public string ConnectionString { get; set; } = XyzDb.DefaultConnectionString;

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
