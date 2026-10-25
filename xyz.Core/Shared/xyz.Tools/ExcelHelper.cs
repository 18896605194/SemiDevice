using System.Globalization;
using System.Text;
using MiniExcelLibs;
using MiniExcelLibs.Attributes;
using MiniExcelLibs.Csv;
using MiniExcelLibs.OpenXml;

namespace xyz.Tools;

/// <summary>
/// Excel、CSV 读写（MiniExcel），前后端共用：数据曲线导出、IO 点表都走这里
/// </summary>
public static class ExcelHelper
{
    /// <summary>
    /// 按表写 xlsx：各表同一组列，第一行列名并冻结，行边枚举边写
    /// </summary>
    public static void Save(string path, IReadOnlyList<ExcelColumn> columns, IReadOnlyList<ExcelSheet> sheets)
    {
        var names = columns.Select(column => column.Name).ToArray();
        var content = new Dictionary<string, object>();
        foreach (var sheet in sheets)
        {
            // 按读取器给：某张表一行都没有也照样写出表头
            content[sheet.Name] = new ExcelRowsReader(names, sheet.Rows);
        }

        var configuration = new OpenXmlConfiguration
        {
            FreezeRowCount = 1,
            DynamicColumns = columns.Where(column => column.Format is not null || column.Width is not null).Select(ToDynamicColumn).ToArray(),
        };
        WriteThroughTemp(path, temp => MiniExcel.SaveAs(temp, content, excelType: ExcelType.XLSX, configuration: configuration, overwriteFile: true));
    }

    /// <summary>
    /// 读 CSV：第一行是表头，每行按表头取值（表头不分大小写），格子去掉首尾空格；带引号的格子里可以有逗号
    /// </summary>
    public static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var rows = new List<Dictionary<string, string>>();
        foreach (var row in MiniExcel.Query(path, useHeaderRow: true, excelType: ExcelType.CSV).Cast<IDictionary<string, object?>>())
        {
            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (header, value) in row)
            {
                cells[header.Trim()] = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            }

            rows.Add(cells);
        }

        return rows;
    }

    /// <summary>
    /// 写 CSV：第一行列名，行边枚举边写；UTF-8 带 BOM，Excel 打开中文不乱
    /// </summary>
    public static void SaveCsv(string path, IReadOnlyList<string> columns, IEnumerable<object?[]> rows)
    {
        var configuration = new CsvConfiguration
        {
            StreamWriterFunc = stream => new StreamWriter(stream, new UTF8Encoding(true)),
        };
        // 按读取器给：一行都没有也照样写出表头
        var reader = new ExcelRowsReader(columns.ToArray(), rows);
        WriteThroughTemp(path, temp => MiniExcel.SaveAs(temp, reader, excelType: ExcelType.CSV, configuration: configuration, overwriteFile: true));
    }

    /// <summary>
    /// 先写旁边的临时文件、写完再换上去：中途出错、取消都不留半个文件，也不先冲掉原来同名的文件
    /// </summary>
    private static void WriteThroughTemp(string path, Action<string> write)
    {
        string temp = path + ".tmp";
        try
        {
            write(temp);
            File.Move(temp, path, true);
        }
        catch
        {
            DeleteQuietly(temp);
            throw;
        }
    }

    private static DynamicExcelColumn ToDynamicColumn(ExcelColumn column)
    {
        var dynamic = new DynamicExcelColumn(column.Name);
        if (column.Format is not null)
        {
            dynamic.Format = column.Format;
        }

        if (column.Width is not null)
        {
            dynamic.Width = column.Width.Value;
        }

        return dynamic;
    }

    /// <summary>
    /// 删掉写了一半的临时文件；删不掉（被占着）就留着，不盖住原来的错误
    /// </summary>
    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
