using ProtoBuf.Grpc;
using xyz.Elements.Elements;
using xyz.Elements.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Systems;

/// <summary>
/// 系统设置
/// </summary>
public class SystemService : BaseService, ISystemService
{
    private readonly ISystemModule? _system;

    private readonly IAccount? _account;

    public SystemService()
    {
        _system = IocHelper.GetService<ISystemModule>();
        _account = IocHelper.GetService<IAccount>();
    }

    public Task<RpcResponse> GetSettingsAsync(RpcRequest request, CallContext context = default)
    {
        var language = _system?.Language;
        var settings = new SystemSettingsDto
        {
            Language = string.IsNullOrWhiteSpace(language) ? SystemSettingsDto.DefaultLanguage : language,
            // 配了安全信号（Safety 节点下有子节点）时，它排在最前面：IO 页给它单独一页。整机不算模块，不在里面。
            Modules =
            [
                .. IocHelper.GetServices<ISafety>().Where(safety => safety.HasSignals).Select(safety => safety.Name),
                .. IocHelper.GetServices<IModule>().Select(module => module.Name),
            ],
            Chambers = [.. IocHelper.GetServices<IChamber>().Select(module => module.Name)],
            LoadPorts = [.. IocHelper.GetServices<ILoadPort>().Select(module => module.Name)],
            Robots = [.. IocHelper.GetServices<IRobot>().Select(module => module.Name)],
            // 没配 Account 节点就是不要登录
            IsLoginEnabled = _account is not null && _account.IsEnable,
        };

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(settings)));
    }
}
