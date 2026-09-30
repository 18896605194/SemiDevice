using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 数据曲线：后端每个采样周期（默认 1 秒）把全部 IO 点和能画成曲线的 SV 整行入库，这里按名字、按时间段查。
/// </summary>
[ServiceContract]
public interface IDataChartService
{
    /// <summary>
    /// 能选哪些信号。Data 为 DataChartSignalsDto 的 JSON；没装数据曲线回 datachart.not_installed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetSignalsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 按名字查一段时间的曲线（点多了后端抽稀）。Data 为 DataChartResultDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> QueryAsync(DataChartQuery query, CallContext context = default);
}
