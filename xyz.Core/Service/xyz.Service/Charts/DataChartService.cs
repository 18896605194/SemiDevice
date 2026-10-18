using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Charts;

/// <summary>
/// 数据曲线 gRPC 服务：信号表和查询都走数据曲线组件（IDataChartComponent）。读库放线程池，不占 gRPC 线程；
/// 界面缩放、平移会接连发查询，旧的那个客户端一取消，这边读到一半也就停了。
/// </summary>
public class DataChartService : IDataChartService
{
    private readonly IDataChartComponent? _chart;

    private readonly IMapperComponent _mapper;

    public DataChartService()
    {
        _chart = IocHelper.GetService<IDataChartComponent>();
        _mapper = IocHelper.GetService<IMapperComponent>() ?? throw new InvalidOperationException("sc.xml 没配映射组件（Mapper）");
    }

    /// <summary>
    /// 能选的信号就是正在记的那些——sc.xml 里配置了的（组件上的 SV + 组件绑的 IO），顺序、层级跟 sc.xml 一样；
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
                var names = (query.Names ?? [])
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(Math.Max(1, _chart.QueryMaxSignals))
                    .ToList();
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
}
