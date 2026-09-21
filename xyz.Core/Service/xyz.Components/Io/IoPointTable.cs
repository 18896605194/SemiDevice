using System.Globalization;
using xyz.Common.Log;

namespace xyz.Components.Io;

/// <summary>
/// 一类 IO（DI/DO/AI/AO）的点表：按索引和按名字各存一份，取点不用遍历。
/// </summary>
public sealed class IoPointTable
{
    private readonly Dictionary<string, IoPoint> _byName = new(StringComparer.OrdinalIgnoreCase);

    public IoPointTable(string name)
    {
        Name = name;
    }

    /// <summary>区名（DI/DO/AI/AO）。</summary>
    public string Name { get; }

    public IReadOnlyList<IoPoint> Points { get; private set; } = [];

    /// <summary>按名字取点；点表里没有返回 null。</summary>
    public IoPoint? Find(string pointName)
    {
        return _byName.GetValueOrDefault(pointName);
    }

    /// <summary>
    /// 读点表 csv。格式跟仿真器同一份：表头按列名定位（Index/Module/Component/Name/Tag/Description，
    /// AI/AO 另有 Unit/PhysicalMax/PhysicalMin/LogicalMax/LogicalMin），列顺序不限。
    /// 文件不在就是空表——装机还没给点表时照常空转，不拦启动。
    /// </summary>
    public void Load(string csvPath)
    {
        var points = new List<IoPoint>();
        _byName.Clear();

        if (!File.Exists(csvPath))
        {
            Points = points;
            return;
        }

        string[] lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2)
        {
            Points = points;
            return;
        }

        string[] headers = lines[0].Split(',');
        int indexColumn = FindColumn(headers, "Index");
        if (indexColumn < 0)
        {
            LogHelper.Warn("Io", $"{csvPath} 缺少 Index 列，本区不采集");
            Points = points;
            return;
        }

        int nameColumn = FindColumn(headers, "Name");
        int moduleColumn = FindColumn(headers, "Module");
        int componentColumn = FindColumn(headers, "Component");
        int tagColumn = FindColumn(headers, "Tag");
        int descriptionColumn = FindColumn(headers, "Description");
        int unitColumn = FindColumn(headers, "Unit");
        int physicalMaxColumn = FindColumn(headers, "PhysicalMax");
        int physicalMinColumn = FindColumn(headers, "PhysicalMin");
        int logicalMaxColumn = FindColumn(headers, "LogicalMax");
        int logicalMinColumn = FindColumn(headers, "LogicalMin");

        for (int line = 1; line < lines.Length; line++)
        {
            if (string.IsNullOrWhiteSpace(lines[line]))
            {
                continue;
            }

            string[] fields = lines[line].Split(',');
            if (indexColumn >= fields.Length
                || !int.TryParse(fields[indexColumn].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                || index < 0)
            {
                continue;
            }

            var point = new IoPoint
            {
                Index = index,
                Name = Cell(fields, nameColumn),
                Module = Cell(fields, moduleColumn),
                Component = Cell(fields, componentColumn),
                Tag = Cell(fields, tagColumn),
                Description = Cell(fields, descriptionColumn),
                Unit = Cell(fields, unitColumn),
                PhysicalMax = Number(fields, physicalMaxColumn),
                PhysicalMin = Number(fields, physicalMinColumn),
                LogicalMax = Number(fields, logicalMaxColumn),
                LogicalMin = Number(fields, logicalMinColumn),
            };

            points.Add(point);

            // 点名重复是点表的错：业务按名字取点，重名就会取错点，这儿必须吭声。
            if (point.Name.Length > 0 && !_byName.TryAdd(point.Name, point))
            {
                LogHelper.Warn("Io", $"{Name} 点表里 {point.Name} 重名（索引 {index}），后一条不参与按名取点");
            }
        }

        Points = points;
    }

    private static string Cell(string[] fields, int column)
    {
        return column < 0 || column >= fields.Length ? string.Empty : fields[column].Trim();
    }

    private static double Number(string[] fields, int column)
    {
        return double.TryParse(Cell(fields, column), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0;
    }

    /// <summary>表头带 BOM 的第一列也要能认出来。</summary>
    private static int FindColumn(string[] headers, string columnName)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            if (string.Equals(headers[i].Trim().TrimStart('﻿'), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
