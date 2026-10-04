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
/// 顺带把装了哪些模块（其中哪些是腔体、LoadPort、机械手）告诉客户端——按模块分的界面（IO、腔体手动、主界面的 LoadPort 页签和调度图）
/// 照它生成，sc.xml 里没配的不出现。
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
            // 配了安全信号（Safety 节点下有子节点）时，它排在最前面：IO 页给它单独一页。
            Modules =
            [
                .. _roots.OfType<SafetyComponent>().Where(safety => safety.Children.Count > 0).Select(safety => safety.Name),
                .. _roots.OfType<BaseModule>().Where(module => module.IsEnabled).Select(module => module.Name),
            ],
            Chambers = [.. _roots.OfType<BaseChamberModule>().Where(module => module.IsEnabled).Select(module => module.Name)],
            LoadPorts = [.. _roots.OfType<BaseLoadPortModule>().Where(module => module.IsEnabled).Select(module => module.Name)],
            Robots = [.. _roots.OfType<BaseRobotModule>().Where(module => module.IsEnabled).Select(module => module.Name)],
        };

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(settings)));
    }
}
