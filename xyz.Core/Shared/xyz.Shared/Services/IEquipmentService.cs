using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 整机操作
/// </summary>
[ServiceContract]
public interface IEquipmentService
{
    [OperationContract]
    Task<RpcResponse> InitAsync(RpcRequest request, CallContext context = default);
    [OperationContract]
    Task<RpcResponse> AutoAsync(RpcRequest request, CallContext context = default);


    [OperationContract]
    Task<RpcResponse> ManualAsync(RpcRequest request, CallContext context = default);


    [OperationContract]
    Task<RpcResponse> StopAsync(RpcRequest request, CallContext context = default);


    [OperationContract]
    Task<RpcResponse> ReturnAllAsync(RpcRequest request, CallContext context = default);


    [OperationContract]
    Task<RpcResponse> SetWaferSizeAsync(RpcRequest request, CallContext context = default);
}
