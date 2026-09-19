using ProtoBuf.Grpc;
using System.ServiceModel;
using xyz.Shared.Dtos;

namespace xyz.Shared.Services;

/// <summary>
/// 系统设置服务契约：sc.xml 的 System 节点（界面语言等）。
/// </summary>
[ServiceContract]
public interface ISystemService
{
    /// <summary>
    /// 系统设置。Data 为 SystemSettingsDto 的 JSON。
    /// </summary>
    [OperationContract]
    Task<RpcResponse> GetSettingsAsync(RpcRequest request, CallContext context = default);
}
