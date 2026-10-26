using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Charts;

/// <summary>
/// 数据曲线服务
/// </summary>
public class DataChartService : IDataChartService
{
    private readonly IDataChart? _chart;

    private readonly IMapper _mapper;

    public DataChartService()
    {
        _chart = IocHelper.GetService<IDataChart>();
        _mapper = IocHelper.GetService<IMapper>() ?? throw new InvalidOperationException("machine.xml 没配映射组件（Mapper）");
    }

    /// <summary>
    /// machine.xml 里面就是记录的信号点
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public Task<RpcResponse> GetSignalsAsync(RpcRequest request, CallContext context = default)
    {
        if (_chart is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.DataChartNotInstalled, []));
        }

        var result = new DataChartSignalsDto
        {
            Signals = _chart.Signals.Select(signal => _mapper.Map<DataChartSignalDto>(signal)).ToList(),
            MaxSignals = Math.Max(1, _chart.QueryMaxSignals),
            SampleIntervalMs = _chart.SampleIntervalMs,
            ScreenshotDirectory = _chart.ScreenshotDirectory,
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result)));
    }

    /// <summary>
    /// 查询，会被多次调用，鼠标滚轮的时候也会查询，整个曲线是一种抽稀的模式
    /// </summary>
    /// <param name="query"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public Task<RpcResponse> QueryAsync(DataChartQuery query, CallContext context = default)
    {
        var token = context.CancellationToken;
        return Task.Run(() =>
        {
            if (_chart is null)
            {
                return RpcResponse.Fail(ErrorCodes.DataChartNotInstalled, []);
            }

            try
            {
                var names = NamesOf(query, _chart);
                var result = _chart.Query(names, query.Start, query.End, token); //按照时间
                return RpcResponse.Ok(JsonHelper.Serialize(_mapper.Map<DataChartResultDto>(result))); //返回json
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return RpcResponse.Fail(ErrorCodes.HistoryQueryFailed, [exception.Message]);
            }
        }, token);
    }

    /// <summary>
    /// 导出，一段一段读取 不是一起读取 用的yied模式
    /// </summary>
    public async IAsyncEnumerable<RpcResponse> ExportAsync(DataChartQuery query, CallContext context = default)
    {
        var token = context.CancellationToken;
        if (_chart is null)
        {
            yield return RpcResponse.Fail(ErrorCodes.DataChartNotInstalled, []);
            yield break;
        }

        using var slices = _chart.Export(NamesOf(query, _chart), query.Start, query.End, token).GetEnumerator();
        while (true)
        {
            var (slice, error) = await Task.Run(() => Next(slices), token);
            if (error is not null)
            {
                yield return RpcResponse.Fail(ErrorCodes.HistoryQueryFailed, [error]);
                yield break;
            }

            if (slice is null)
            {
                yield break;
            }

            yield return RpcResponse.Ok(JsonHelper.Serialize(_mapper.Map<DataChartExportDto>(slice)));
        }
    }

    /// <summary>
    /// 要查的名字：去空、去重，超过最多条数的只要前面的
    /// </summary>
    private static List<string> NamesOf(DataChartQuery query, IDataChart chart)
    {
        return (query.Names ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, chart.QueryMaxSignals))
            .ToList();
    }

    /// <summary>
    /// 读下一段：读完了两个都是 null；读库出错带回原因（迭代器里不能在 try 里 yield，所以在这里接住）
    /// </summary>
    private static (DataExport? Slice, string? Error) Next(IEnumerator<DataExport> slices)
    {
        try
        {
            if (slices.MoveNext())
            {
                return (slices.Current, null);
            }

            return (null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (null, exception.Message);
        }
    }
}
