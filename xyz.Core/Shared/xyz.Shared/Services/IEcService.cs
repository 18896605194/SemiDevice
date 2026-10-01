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
    /// 全部 EC 项的定义与当前值，先后跟 sc.xml 的组件层级一样。Data 为 List&lt;EcItemDto&gt; 的 JSON；没装 EC 组件时为空表。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetDefinitionsAsync(RpcRequest request, CallContext context = default);

    /// <summary>
    /// 改一项 EC 的值：按声明的格式、上下限、可选值查过才改，改完写回 ec.xml 并立即生效。
    /// 成功时 Data 为改完的 EcItemDto 的 JSON；失败回 ec.not_installed / ec.not_found / ec.invalid_format /
    /// ec.out_of_range / ec.invalid_option / ec.save_failed。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> SetValueAsync(EcSetRequest request, CallContext context = default);
}
