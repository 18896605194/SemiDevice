using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// EC（在线可调参数）服务契约。
/// </summary>
[ServiceContract]
public interface IEcService
{
    /// <summary>
    /// 全部 EC 项的定义与当前值。Data 为 List&lt;EcItemDto&gt; 的 JSON；没装 EC 组件时为空表。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetDefinitionsAsync(RpcRequest request, CallContext context = default);
}
