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
    /// 全部 EC 项的定义与当前值，先后跟 machine.xml 的组件层级一样。Data 为 List&lt;EcItemDto&gt; 的 JSON；没装 EC 组件时为空表。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetDefinitionsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 修改一项 EC 的值（EcItemDto）
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetValueAsync(RpcRequest request, CallContext context = default);
}
