using System.Globalization;
using xyz.Common.Log;
using xyz.Tools;

namespace xyz.Elements.Models;

/// <summary>
/// 一类 IO（DI/DO/AI/AO）的点表：按 PLC 索引取点，列表用于采集和展示。
/// </summary>
public sealed class IoPointTable
{
    private readonly Dictionary<int, IoPoint> _byIndex = new();

    public IoPointTable(string name)
    {
        Name = name;
    }

    /// <summary>区名（DI/DO/AI/AO）。</summary>
    public string Name { get; }

    public IReadOnlyList<IoPoint> Points { get; private set; } = [];

    /// <summary>按 PLC 索引取点；点表里没有返回 null，索引不要求连续。</summary>
    public IoPoint? Find(int index)
    {
        return _byIndex.GetValueOrDefault(index);
    }

    /// <summary>
    /// 读点表 csv（ExcelHelper.ReadCsv）。格式跟仿真器同一份：表头按列名定位（Index/Module/Component/Name/Tag/Description，
    /// AI/AO 另有 Unit/PhysicalMax/PhysicalMin/LogicalMax/LogicalMin），列顺序不限、不分大小写，带引号的格子里可以有逗号。
    /// 文件不在就是空表——装机还没给点表时照常空转，不拦启动。点表有问题的日志记在 owner（IO 元件）名下。
    /// </summary>
    public void Load(string csvPath, ILogSource owner)
    {
        var points = new List<IoPoint>();
        _byIndex.Clear();

        if (!File.Exists(csvPath))
        {
            Points = points;
            return;
        }

        var rows = ExcelHelper.ReadCsv(csvPath);
        if (rows.Count > 0 && !rows[0].ContainsKey("Index"))
        {
            LogHelper.Warn(owner, $"{csvPath} 缺少 Index 列，本区不采集");
            Points = points;
            return;
        }

        for (int row = 0; row < rows.Count; row++)
        {
            var cells = rows[row];
            if (!int.TryParse(Cell(cells, "Index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0)
            {
                continue;
            }

            var point = new IoPoint
            {
                Index = index,
                Name = Cell(cells, "Name"),
                Module = Cell(cells, "Module"),
                Element = Cell(cells, "Component"),
                Tag = Cell(cells, "Tag"),
                Description = Cell(cells, "Description"),
                Unit = Cell(cells, "Unit"),
                PhysicalMax = Number(cells, "PhysicalMax"),
                PhysicalMin = Number(cells, "PhysicalMin"),
                LogicalMax = Number(cells, "LogicalMax"),
                LogicalMin = Number(cells, "LogicalMin"),
            };

            if (!_byIndex.TryAdd(index, point))
            {
                // 表头是第 1 行，数据从第 2 行起
                LogHelper.Warn(owner, $"{Name} 点表索引 {index} 重复，忽略第 {row + 2} 行");
                continue;
            }

            points.Add(point);
        }

        Points = points;
    }

    private static string Cell(Dictionary<string, string> cells, string column)
    {
        return cells.GetValueOrDefault(column) ?? string.Empty;
    }

    private static double Number(Dictionary<string, string> cells, string column)
    {
        if (double.TryParse(Cell(cells, column), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return value;
        }

        return 0;
    }
}
