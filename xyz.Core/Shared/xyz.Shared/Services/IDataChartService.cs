using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;


[ServiceContract]
public interface IDataChartService
{
    [OperationContract]
    Task<RpcResponse> GetSignalsAsync(RpcRequest request, CallContext context = default);

    [OperationContract]
    Task<RpcResponse> QueryAsync(DataChartQuery query, CallContext context = default);

    [OperationContract]
    IAsyncEnumerable<RpcResponse> ExportAsync(DataChartQuery query, CallContext context = default);
}
