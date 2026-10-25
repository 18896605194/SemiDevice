using Grpc.Core;
using ProtoBuf.Grpc;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.DataCenter.Models;

/// <summary>
/// 数据曲线导出 Excel：收后端的导出流，一天一张表（时间 + 每条曲线一列），收一段写一段
/// </summary>
public static class DataChartExport
{
    /// <summary>
    /// 没数据时回的错误码（客户端自己的，界面按它提示"没有生成文件"）
    /// </summary>
    public const string EmptyCode = "datachart.export_empty";

    /// <summary>
    /// 时间列的显示格式：1 秒一行，要带秒
    /// </summary>
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// 时间列宽（字符数），放得下 TimeFormat
    /// </summary>
    private const double TimeWidth = 20;

    /// <summary>
    /// 导出 [start, end)（本机时间）这些曲线的原始数据到 path，在后台线程跑、进度 0～100 报给 progress。
    /// 后端没成回它的失败（错误码界面线程上再翻），一行都没有回 EmptyCode、不建文件；写文件出错照抛。
    /// </summary>
    public static Task<RpcResponse> SaveAsync(IDataChartService service, IReadOnlyList<string> names, DateTime start, DateTime end,
        string path, string timeHeader, IProgress<double> progress, CancellationToken token)
    {
        return Task.Run(() => Save(service, names, start, end, path, timeHeader, progress, token), token);
    }

    private static RpcResponse Save(IDataChartService service, IReadOnlyList<string> names, DateTime start, DateTime end,
        string path, string timeHeader, IProgress<double> progress, CancellationToken token)
    {
        var query = new DataChartQuery { Start = start, End = end, Names = names.ToList() };
        var responses = service.ExportAsync(query, new CallContext(new CallOptions(cancellationToken: token)));
        RpcResponse? failure = null;
        using var rows = Rows(responses.ToBlockingEnumerable(token), names, start, end, progress, response => failure = response)
            .GetEnumerator();
        bool hasRow = false;

        // 表按天先排好（MiniExcel 要一开始就拿到全部表），每张表的行到写它的时候才从流里取：
        // 取到不是这天的行就停，留给下一张表
        IEnumerable<object?[]> Day(DateTime day)
        {
            while (hasRow && rows.Current.Time.Date == day)
            {
                yield return rows.Current.Cells;
                hasRow = rows.MoveNext();
            }
        }

        try
        {
            hasRow = rows.MoveNext();
            if (!hasRow)
            {
                return RpcResponse.Fail(EmptyCode, []);
            }

            var sheets = new List<ExcelSheet>();
            for (var day = start.Date; day <= end.AddTicks(-1).Date; day = day.AddDays(1))
            {
                sheets.Add(new ExcelSheet(day.ToString("yyyy-MM-dd"), Day(day)));
            }

            var columns = new List<ExcelColumn> { new(timeHeader, TimeFormat, TimeWidth) };
            columns.AddRange(names.Select(name => new ExcelColumn(name)));
            ExcelHelper.Save(path, columns, sheets);
        }
        catch (InvalidOperationException) when (failure is not null)
        {
            // 后端回了失败：Rows 抛出来打断写文件（临时文件删了，原来同名的文件不动）
            return failure;
        }

        progress.Report(100);
        return RpcResponse.Ok(path);
    }

    /// <summary>
    /// 流里的每一段摊成一行一行：第 0 格是时间（本机），后面按 names 的顺序，没读到的留 null；
    /// 每段收完按它最后一行的时刻报进度。后端回了失败就交给 onFailure、再抛出来打断写文件。
    /// </summary>
    private static IEnumerable<(DateTime Time, object?[] Cells)> Rows(IEnumerable<RpcResponse> responses, IReadOnlyList<string> names,
        DateTime start, DateTime end, IProgress<double> progress, Action<RpcResponse> onFailure)
    {
        var columnOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < names.Count; index++)
        {
            columnOf[names[index]] = index + 1;
        }

        long from = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        long to = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        foreach (var response in responses)
        {
            if (!response.Success)
            {
                onFailure(response);
                throw new InvalidOperationException(response.Code);
            }

            var data = response.DeserializeData<DataChartExportDto>();
            var columns = data.Names.Select(name => columnOf.GetValueOrDefault(name)).ToArray();
            for (int row = 0; row < data.Times.Count; row++)
            {
                var time = DateTimeOffset.FromUnixTimeMilliseconds(data.Times[row]).LocalDateTime;
                var cells = new object?[names.Count + 1];
                cells[0] = time;
                for (int series = 0; series < columns.Length; series++)
                {
                    double? value = data.Values[series][row];
                    if (columns[series] > 0 && value is not null)
                    {
                        cells[columns[series]] = value.Value;
                    }
                }

                yield return (time, cells);
            }

            if (data.Times.Count > 0 && to > from)
            {
                progress.Report((data.Times[^1] - from) * 100.0 / (to - from));
            }
        }
    }
}
