using SqlSugar;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Database.DbProvider;

namespace xyz.Components.Components;

/// <summary>
/// 数据库组件：保存数据库连接字符串（SC 装机常量），并提供数据库访问入口。
/// </summary>
[Component(description: "数据库组件")]
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
    /// 根据当前连接字符串和数据库类型创建 SqlSugar 数据库客户端。
    /// </summary>
    public SqlSugarClient CreateDb()
    {
        return XyzDb.Create(ConnectionString, DbType);
    }

    #endregion
}
