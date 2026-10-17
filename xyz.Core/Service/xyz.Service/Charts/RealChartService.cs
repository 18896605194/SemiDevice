using ProtoBuf.Grpc;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Charts;

/// <summary>
/// 实时曲线 gRPC 服务：信号表、最近一段都走实时曲线组件（IRealChartComponent）；每帧不走这里，走事件流。
/// </summary>
public class RealChartService : IRealChartService
{
    private readonly IRealChartComponent? _realChart;

    private readonly IDataChartComponent? _dataChart;

    private readonly IMapperComponent _mapper;

    public RealChartService()
    {
        _realChart = ComponentBase.Find<IRealChartComponent>();
        _dataChart = ComponentBase.Find<IDataChartComponent>();
        _mapper = ComponentBase.Find<IMapperComponent>() ?? throw new InvalidOperationException("sc.xml 没配映射组件（Mapper）");
    }

    public Task<RpcResponse> GetLayoutAsync(RpcRequest request, CallContext context = default)
    {
        if (_realChart is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.RealChartNotInstalled, []));
        }

        var layout = new RealChartLayoutDto
        {
            Session = _realChart.Session,
            SampleIntervalMs = _realChart.SampleIntervalMs,
            WindowSeconds = _realChart.WindowSeconds,
            MaxSignals = Math.Max(1, _dataChart?.QueryMaxSignals ?? 20),
            ScreenshotDirectory = _dataChart?.ScreenshotDirectory ?? string.Empty,
            Signals = _realChart.Signals.Select(signal => _mapper.Map<DataChartSignalDto>(signal)).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(layout)));
    }

    public Task<RpcResponse> GetRecentAsync(RealChartRecentQuery query, CallContext context = default)
    {
        if (_realChart is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.RealChartNotInstalled, []));
        }

        var indexes = _realChart.Signals
            .Select((signal, index) => (signal.Name, Index: index))
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var wanted = (query.Names ?? [])
            .Where(indexes.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var recent = _realChart.Recent();
        var result = new RealChartRecentDto
        {
            Session = _realChart.Session,
            Times = recent.Select(record => record.Time).ToList(),
            Series = wanted.Select(name => new DataChartSeriesDto
            {
                Name = name,
                Values = recent.Select(record => ToFloat(record.Values[indexes[name]])).ToList(),
            }).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result)));
    }

    /// <summary>
    /// 值转单精度（曲线够用，包小一半）
    /// </summary>
    private static float? ToFloat(double? value)
    {
        if (value is null)
        {
            return null;
        }

        return (float)value.Value;
    }
}
