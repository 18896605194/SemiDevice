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
    /// 按名字查 [Start, End) 的曲线（本机时间）。参数：Start（DateTime）、End（DateTime）、Names（string 列表）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> QueryAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 导出 [Start, End) 的原始行，一段一条消息。参数：Start（DateTime）、End（DateTime）、Names（string 列表）
    /// </summary>
    [OperationContract]
    IAsyncEnumerable<RpcResponse> ExportAsync(RpcRequest request, CallContext context = default);
}
