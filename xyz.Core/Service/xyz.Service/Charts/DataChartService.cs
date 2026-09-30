using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Charts;

/// <summary>
/// 数据曲线 gRPC 服务：信号表和查询都走数据曲线组件（DataChartComponent.Current）。读库放线程池，不占 gRPC 线程；
/// 界面缩放、平移会接连发查询，旧的那个客户端一取消，这边读到一半也就停了。
/// </summary>
public class DataChartService : IDataChartService
{
    public Task<RpcResponse> GetSignalsAsync(RpcRequest request, CallContext context = default)
    {
        return Task.Run(() =>
        {
            if (DataChartComponent.Current is not { } chart)
            {
                return RpcResponse.Fail(ErrorCodes.DataChartNotInstalled, []);
            }

            try
            {
                var current = chart.Signals.ToDictionary(signal => signal.Name, StringComparer.OrdinalIgnoreCase);
                var result = new DataChartSignalsDto
                {
                    Signals = chart.RecordedNames()
                        .Select(name => current.TryGetValue(name, out var signal)
                            ? signal.ToDto()
                            : new DataChartSignalDto { Name = name })
                        .ToList(),
                    MaxSignals = Math.Max(1, chart.QueryMaxSignals),
                    SampleIntervalMs = chart.SampleIntervalMs,
                };
                return RpcResponse.Ok(JsonHelper.Serialize(result));
            }
            catch (Exception exception)
            {
                return RpcResponse.Fail(ErrorCodes.HistoryQueryFailed, [exception.Message]);
            }
        });
    }

    public Task<RpcResponse> QueryAsync(DataChartQuery query, CallContext context = default)
    {
        var token = context.CancellationToken;
        return Task.Run(() =>
        {
            if (DataChartComponent.Current is not { } chart)
            {
                return RpcResponse.Fail(ErrorCodes.DataChartNotInstalled, []);
            }

            try
            {
                var names = (query.Names ?? [])
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(Math.Max(1, chart.QueryMaxSignals))
                    .ToList();
                var result = chart.Query(names, query.Start, query.End, token);
                return RpcResponse.Ok(JsonHelper.Serialize(result.ToDto()));
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
