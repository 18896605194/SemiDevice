using ProtoBuf.Grpc;
using xyz.Components;
using xyz.Components.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Systems;

/// <summary>
/// 系统设置 gRPC 服务：读 sc.xml 的 System 节点（SystemComponent.Current），没装时给默认值。
/// 顺带把装了哪些模块告诉客户端——按模块分的界面（IO 等）照它生成，sc.xml 里没配的不出现。
/// </summary>
public class SystemService : ISystemService
{
    private readonly IReadOnlyList<ComponentBase> _roots;

    public SystemService(IReadOnlyList<ComponentBase> roots)
    {
        _roots = roots;
    }

    public Task<RpcResponse> GetSettingsAsync(RpcRequest request, CallContext context = default)
    {
        var language = SystemComponent.Current?.Language;
        var settings = new SystemSettingsDto
        {
            Language = string.IsNullOrWhiteSpace(language) ? SystemSettingsDto.DefaultLanguage : language,
            Modules = [.. _roots.OfType<BaseModule>().Where(module => module.IsEnabled).Select(module => module.Name)],
        };

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(settings)));
    }
}
