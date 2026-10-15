using System.Data;
using System.Globalization;
using System.Text;
using SqlSugar;
using xyz.Components.Models;

namespace xyz.Components.Components;

/// <summary>
/// 数据曲线的库表：按天一张宽表 DataRecord_yyyyMMdd（本机日期）
/// </summary>
internal static class DataRecordTable
{
    public const string Prefix = "DataRecord_";

    public const string TimeColumn = "Time";

    /// <summary>
    /// SQLite 一张表最多 2000 列，留一列给 Time。
    /// </summary>
    public const int MaxSignals = 1999;

    public static string NameOf(DateTime day)
    {
        return Prefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    public static string Quote(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// 库里全部日表，按日期升序：日期 → 表名。名字对不上格式的表不算。
    /// </summary>
    public static SortedDictionary<DateTime, string> ListTables(ISqlSugarClient db)
    {
        var tables = new SortedDictionary<DateTime, string>();
        // 不用 SqlSugar 的缓存：日表是运行中按天新建的，缓存里会漏掉新表
        foreach (var table in db.DbMaintenance.GetTableInfoList(false))
        {
            string name = table.Name ?? string.Empty;
            if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                && DateTime.TryParseExact(name.AsSpan(Prefix.Length), "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day))
            {
                tables[day] = name;
            }
        }

        return tables;
    }

    /// <summary>
    /// 一张表的信号列（不含 Time），按建表顺序；表不存在返回空。SQLite 列名不分大小写，这里也不分。
    /// </summary>
    public static List<string> ColumnsOf(ISqlSugarClient db, string table)
    {
        var columns = new List<string>();
        foreach (var column in db.DbMaintenance.GetColumnInfosByTableName(table, false))
        {
            string name = column.DbColumnName ?? string.Empty;
            if (name.Length > 0 && !string.Equals(name, TimeColumn, StringComparison.OrdinalIgnoreCase))
            {
                columns.Add(name);
            }
        }

        return columns;
    }

    /// <summary>
    /// 建表、加列自己拼 SQL：列名带点号（Chamber1.Door.DiOpened），SqlSugar 的 CreateTable / AddColumn
    /// 会按点号拆开括成 "Chamber1"."Door"."DiOpened"，SQLite 报语法错。
    /// </summary>
    public static void Create(ISqlSugarClient db, string table, IReadOnlyList<DataSignal> signals)
    {
        var sql = new StringBuilder()
            .Append("CREATE TABLE IF NOT EXISTS ").Append(Quote(table))
            .Append(" (").Append(Quote(TimeColumn)).Append(" INTEGER PRIMARY KEY");
        foreach (var signal in signals)
        {
            sql.Append(", ").Append(Quote(signal.Name)).Append(' ').Append(TypeOf(signal));
        }

        sql.Append(')');
        db.Ado.ExecuteCommand(sql.ToString());
    }

    public static void AddColumn(ISqlSugarClient db, string table, DataSignal signal)
    {
        db.Ado.ExecuteCommand($"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(signal.Name)} {TypeOf(signal)}");
    }

    /// <summary>
    /// 一批行写进一张日表，一个事务。同一时刻已有的行（系统时间往回调过）以新的为准。
    /// </summary>
    public static void Insert(ISqlSugarClient db, string table, IReadOnlyList<DataSignal> signals, IEnumerable<DataRecord> rows)
    {
        var sql = new StringBuilder()
            .Append("INSERT OR REPLACE INTO ").Append(Quote(table))
            .Append(" (").Append(Quote(TimeColumn));
        foreach (var signal in signals)
        {
            sql.Append(", ").Append(Quote(signal.Name));
        }

        sql.Append(") VALUES (@t");
        for (int i = 0; i < signals.Count; i++)
        {
            sql.Append(", @v").Append(i.ToString(CultureInfo.InvariantCulture));
        }

        sql.Append(')');
        string text = sql.ToString();

        db.Ado.BeginTran();
        try
        {
            foreach (var row in rows)
            {
                var parameters = new SugarParameter[signals.Count + 1];
                parameters[0] = new SugarParameter("@t", row.Time);
                for (int i = 0; i < signals.Count; i++)
                {
                    double? cell = row.Values[i];
                    object value = cell is not null ? cell.Value : DBNull.Value;
                    parameters[i + 1] = new SugarParameter("@v" + i.ToString(CultureInfo.InvariantCulture), value);
                }

                db.Ado.ExecuteCommand(text, parameters);
            }

            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    /// <summary>
    /// 读一张日表 [from, to) 之间的行：第 0 列 Time，之后按 columns 的顺序。调用方负责关掉读取器。
    /// </summary>
    public static IDataReader Read(ISqlSugarClient db, string table, IReadOnlyList<string> columns, long from, long to)
    {
        var sql = new StringBuilder("SELECT ").Append(Quote(TimeColumn));
        foreach (var column in columns)
        {
            sql.Append(", ").Append(Quote(column));
        }

        sql.Append(" FROM ").Append(Quote(table))
            .Append(" WHERE ").Append(Quote(TimeColumn)).Append(" >= @from AND ").Append(Quote(TimeColumn)).Append(" < @to")
            .Append(" ORDER BY ").Append(Quote(TimeColumn));
        return db.Ado.GetDataReader(sql.ToString(), new SugarParameter("@from", from), new SugarParameter("@to", to));
    }

    private static string TypeOf(DataSignal signal)
    {
        return signal.IsDigital ? "INTEGER" : "REAL";
    }
}
