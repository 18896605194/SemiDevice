using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 实时曲线：每帧由后端主动推（EventBus，token = RealChartFrameDto.EventToken）；这里只拉信号表和最近一段。
/// </summary>
[ServiceContract]
public interface IRealChartService
{
    /// <summary>
    /// 信号表（每帧值的顺序）。Data 为 RealChartLayoutDto 的 JSON；没装实时曲线回 realchart.not_installed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetLayoutAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 这几个信号最近一段的值（后端内存里留的，WindowSeconds 秒）。Data 为 RealChartRecentDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetRecentAsync(RealChartRecentQuery query, CallContext context = default);
}
