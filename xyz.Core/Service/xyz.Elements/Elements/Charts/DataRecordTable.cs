using System.Data;
using System.Globalization;
using SqlSugar;
using xyz.Elements.Models;

namespace xyz.Elements.Elements;

/// <summary>
/// 数据曲线的库表：按天一张宽表 DataRecord_yyyyMMdd（本机日期）
/// 全部走 SqlSugar 的接口、不写 SQL，换库时这里不用改；列名就是信号名（层级用 __ 隔开，见 <see cref="DataSignal.Separator"/>）。
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
    /// 建表：Time（UTC 毫秒）做主键，每个信号一列。
    /// </summary>
    public static void Create(ISqlSugarClient db, string table, IReadOnlyList<DataSignal> signals)
    {
        var columns = new List<DbColumnInfo>
        {
            new()
            {
                TableName = table,
                DbColumnName = TimeColumn,
                DataType = db.Ado.DbBind.GetDbTypeName(nameof(Int64)),
                IsPrimarykey = true,
                IsNullable = false,
            },
        };
        columns.AddRange(signals.Select(signal => ColumnOf(db, table, signal)));
        db.DbMaintenance.CreateTable(table, columns, true);
    }

    public static void AddColumn(ISqlSugarClient db, string table, DataSignal signal)
    {
        db.DbMaintenance.AddColumn(table, ColumnOf(db, table, signal));
    }

    /// <summary>
    /// 一批行写进一张日表，一个事务：先删掉同一时刻已有的行（系统时间往回调过），再插，以新的为准。
    /// </summary>
    public static void Insert(ISqlSugarClient db, string table, IReadOnlyList<DataSignal> signals, IEnumerable<DataRecord> rows)
    {
        var batch = rows.ToList();
        if (batch.Count == 0)
        {
            return;
        }

        var sameTimes = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = TimeColumn,
                ConditionalType = ConditionalType.In,
                FieldValue = string.Join(",", batch.Select(row => row.Time.ToString(CultureInfo.InvariantCulture))),
                CSharpTypeName = "long",
            },
        };
        var values = batch.Select(row => ToColumns(signals, row)).ToList();
        var result = db.Ado.UseTran(() =>
        {
            db.Deleteable<object>().AS(table).Where(sameTimes).ExecuteCommand();
            db.Insertable(values).AS(table).ExecuteCommand();
        });
        if (!result.IsSuccess)
        {
            throw result.ErrorException;
        }
    }

    /// <summary>
    /// 读一张日表 [from, to) 之间的行，按时间排：第 0 列 Time，之后按 columns 的顺序。调用方负责关掉读取器。
    /// 语句由 SqlSugar 按当前库生成，再用读取器边读边算：整天读成 DataTable 要慢一半多，还占内存。
    /// </summary>
    public static IDataReader Read(ISqlSugarClient db, string table, IReadOnlyList<string> columns, long from, long to)
    {
        var select = new List<SelectModel> { new() { FieldName = TimeColumn, AsName = TimeColumn } };
        select.AddRange(columns.Select(column => new SelectModel { FieldName = column, AsName = column }));
        var range = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = TimeColumn,
                ConditionalType = ConditionalType.GreaterThanOrEqual,
                FieldValue = from.ToString(CultureInfo.InvariantCulture),
                CSharpTypeName = "long",
            },
            new ConditionalModel
            {
                FieldName = TimeColumn,
                ConditionalType = ConditionalType.LessThan,
                FieldValue = to.ToString(CultureInfo.InvariantCulture),
                CSharpTypeName = "long",
            },
        };
        var order = new List<OrderByModel> { new() { FieldName = TimeColumn, OrderByType = OrderByType.Asc } };
        var query = db.Queryable<object>().AS(table).Select(select).Where(range).OrderBy(order).ToSql();
        return db.Ado.GetDataReader(query.Key, query.Value.ToArray());
    }

    /// <summary>
    /// 一行 → 列名到值：开关量存整数 0/1，模拟量存浮点；读不到的放 null（放 DBNull 会被 SqlSugar 写成空字符串）。
    /// </summary>
    private static Dictionary<string, object> ToColumns(IReadOnlyList<DataSignal> signals, DataRecord row)
    {
        var columns = new Dictionary<string, object>(signals.Count + 1) { [TimeColumn] = row.Time };
        for (int i = 0; i < signals.Count; i++)
        {
            double? cell = row.Values[i];
            if (cell is null)
            {
                columns[signals[i].Name] = null!;
            }
            else if (signals[i].IsDigital)
            {
                columns[signals[i].Name] = (long)cell.Value;
            }
            else
            {
                columns[signals[i].Name] = cell.Value;
            }
        }

        return columns;
    }

    /// <summary>
    /// 信号列：开关量整数、模拟量浮点，类型名由 SqlSugar 按当前库给（SQLite 是 bigint / real）。
    /// </summary>
    private static DbColumnInfo ColumnOf(ISqlSugarClient db, string table, DataSignal signal)
    {
        return new DbColumnInfo
        {
            TableName = table,
            DbColumnName = signal.Name,
            DataType = db.Ado.DbBind.GetDbTypeName(signal.IsDigital ? nameof(Int64) : nameof(Double)),
            IsNullable = true,
        };
    }
}
