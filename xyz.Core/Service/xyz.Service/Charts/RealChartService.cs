using ProtoBuf.Grpc;
using xyz.Common;
using xyz.Elements.Elements;
using xyz.Elements.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Charts;

/// <summary>
/// 实时曲线 服务
/// </summary>
public class RealChartService : BaseService, IRealChartService
{
    private readonly IRealChart? _realChart;

    private readonly IDataChart? _dataChart;

    private readonly IMapper _mapper;

    public RealChartService()
    {
        _realChart = IocHelper.GetService<IRealChart>();
        _dataChart = IocHelper.GetService<IDataChart>();
        _mapper = IocHelper.GetService<IMapper>() ?? throw new InvalidOperationException("machine.xml 没配映射元件（Mapper）");
    }

    public Task<RpcResponse> GetLayoutAsync(RpcRequest request, CallContext context = default)
    {
        if (_realChart is null)
        {
            return NotInstalled(ErrorCodes.RealChartNotInstalled);
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

    public Task<RpcResponse> GetRecentAsync(RpcRequest request, CallContext context = default)
    {
        if (_realChart is null)
        {
            return NotInstalled(ErrorCodes.RealChartNotInstalled);
        }

        if (!request.TryGet<List<string>>(RpcParams.Names, out var names))
        {
            return InvalidRequest();
        }

        var indexes = _realChart.Signals
            .Select((signal, index) => (signal.Name, Index: index))
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var wanted = names
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
                // 转单精度：曲线够用，包小一半
                Values = recent.Select(record => ValueHelper.ToFloat(record.Values[indexes[name]])).ToList(),
            }).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(result)));
    }
}
