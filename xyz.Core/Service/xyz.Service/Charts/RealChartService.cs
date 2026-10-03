using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Charts;

/// <summary>
/// 实时曲线 gRPC 服务：信号表、最近一段都走实时曲线组件（RealChartComponent.Current）；每帧不走这里，走事件流。
/// </summary>
public class RealChartService : IRealChartService
{
    public Task<RpcResponse> GetLayoutAsync(RpcRequest request, CallContext context = default)
    {
        var chart = RealChartComponent.Current;
        if (chart is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.RealChartNotInstalled, []));
        }

        var layout = new RealChartLayoutDto
        {
            Session = chart.Session,
            SampleIntervalMs = chart.SampleIntervalMs,
            WindowSeconds = chart.WindowSeconds,
            MaxSignals = Math.Max(1, DataChartComponent.Current?.QueryMaxSignals ?? 20),
            Signals = chart.Signals.Select(signal => signal.ToDto()).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(layout)));
    }

    public Task<RpcResponse> GetRecentAsync(RealChartRecentQuery query, CallContext context = default)
    {
        var chart = RealChartComponent.Current;
        if (chart is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.RealChartNotInstalled, []));
        }

        var indexes = chart.Signals
            .Select((signal, index) => (signal.Name, Index: index))
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var wanted = (query.Names ?? [])
            .Where(indexes.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var recent = chart.Recent();
        var result = new RealChartRecentDto
        {
            Session = chart.Session,
            Times = recent.Select(record => record.Time).ToList(),
            Series = wanted.Select(name => new DataChartSeriesDto
            {
                Name = name,
                Values = recent.Select(record => ChartMapper.ToFloat(record.Values[indexes[name]])).ToList(),
            }).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result)));
    }
}
