using xyz.Components.DataCharts;
using xyz.Shared.Dtos;

namespace xyz.Service.Charts;

/// <summary>
/// 数据曲线、实时曲线的对象转契约：信号表、查询结果、采样行都在组件层，契约层不认识它们，转换放在服务层。
/// </summary>
internal static class ChartMapper
{
    public static DataChartSignalDto ToDto(this DataSignal signal)
    {
        return new DataChartSignalDto
        {
            Name = signal.Name,
            IsDigital = signal.IsDigital,
            Source = signal.Source,
            Unit = signal.Unit,
            Description = signal.Description,
        };
    }

    public static DataChartResultDto ToDto(this DataQueryResult result)
    {
        return new DataChartResultDto
        {
            Times = result.Times.ToList(),
            Series = result.Series.Select(series => new DataChartSeriesDto
            {
                Name = series.Name,
                Values = series.Values.ToList(),
                Min = series.Min,
                Max = series.Max,
                Avg = series.Avg,
                Count = series.Count,
            }).ToList(),
            IsDecimated = result.IsDecimated,
            BucketMs = result.BucketMs,
        };
    }

    /// <summary>
    /// 采样行 → 推给界面的一帧：值转单精度（曲线够用，包小一半）。
    /// </summary>
    public static RealChartFrameDto ToFrame(this DataRecord record, long session)
    {
        return new RealChartFrameDto
        {
            Session = session,
            Time = record.Time,
            Values = record.Values.Select(ToFloat).ToList(),
        };
    }

    public static float? ToFloat(double? value)
    {
        return value is { } number ? (float)number : null;
    }
}
