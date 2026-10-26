using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;


[ServiceContract]
public interface IDataChartService
{
    [OperationContract]
    Task<RpcResponse> GetSignalsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 按名字查询时间段内的曲线
    /// </summary>
    [OperationContract]
    Task<RpcResponse> QueryAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 导出时间段内的原始行
    /// </summary>
    [OperationContract]
    IAsyncEnumerable<RpcResponse> ExportAsync(RpcRequest request, CallContext context = default);
}
