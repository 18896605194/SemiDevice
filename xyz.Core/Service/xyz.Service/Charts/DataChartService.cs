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
/// 数据曲线 gRPC 服务：信号表、查询、导出都走数据曲线组件（IDataChart）。读库放线程池，不占 gRPC 线程；
/// 界面缩放、平移会接连发查询，旧的那个客户端一取消，这边读到一半也就停了；导出是服务端流，一段一段推。
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
    /// 能选的信号就是正在记的那些——machine.xml 里配置了的（组件上的 SV + 组件绑的 IO），顺序、层级跟 machine.xml 一样；
    /// 库里记的也正是这些，所以列出来的一定查得到。
    /// </summary>
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
                var result = _chart.Query(names, query.Start, query.End, token);
                return RpcResponse.Ok(JsonHelper.Serialize(_mapper.Map<DataChartResultDto>(result)));
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
    /// 导出：一段读完推一段（读库放线程池），客户端收一段写一段，两头内存里都只有一段
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
