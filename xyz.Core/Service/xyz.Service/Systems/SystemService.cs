using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Systems;

/// <summary>
/// 系统设置 gRPC 服务：读 sc.xml 的 System 节点（SystemComponent.Current），没装时给默认值。
/// </summary>
public class SystemService : ISystemService
{
    public Task<RpcResponse> GetSettingsAsync(RpcRequest request, CallContext context = default)
    {
        var language = SystemComponent.Current?.Language;
        var settings = new SystemSettingsDto
        {
            Language = string.IsNullOrWhiteSpace(language) ? SystemSettingsDto.DefaultLanguage : language,
        };

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(settings)));
    }
}
