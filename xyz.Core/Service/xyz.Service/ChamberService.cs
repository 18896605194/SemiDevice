using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// 腔体手动操作 gRPC 服务（命令通道）：下发 → 同步等终态 → 码+参数回包。
/// 公共流程（找模块、被拒/超时/终态回包）在 <see cref="BaseService"/>。
/// </summary>
public class ChamberService : BaseService, IChamberService
{
    public ChamberService(IReadOnlyList<ComponentBase> roots) : base(roots)
    {
    }

    public Task<RpcResponse> HomeAsync(string module)
    {
        var chamber = FindModule<BaseChamberModule>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, chamber, chamber.Home(), chamber.HomeTimeout);
    }

    public Task<RpcResponse> ResetAsync(string module)
    {
        var chamber = FindModule<BaseChamberModule>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, chamber, chamber.Reset(), chamber.ResetTimeout);
    }

    public Task<RpcResponse> AbortAsync(string module)
    {
        var chamber = FindModule<BaseChamberModule>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, chamber, chamber.Abort(), chamber.AbortTimeout);
    }

    public Task<RpcResponse> ProcessAsync(ChamberProcessRequest request)
    {
        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        var module = request.Module ?? string.Empty;
        var chamber = FindModule<BaseChamberModule>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        var recipe = request.Recipe ?? string.Empty;
        if (string.IsNullOrWhiteSpace(recipe))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.RecipeRequired, [module]));
        }

        return RunOperation(module, chamber, chamber.Process(recipe.Trim()), chamber.ProcessTimeout);
    }

    /// <summary>
    /// 上线/下线只改模块模式 Mode（不经设备），置位即成功；界面经事件流刷新模式灯。
    /// </summary>
    public Task<RpcResponse> OnlineAsync(string module)
    {
        var chamber = FindModule<BaseChamberModule>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        chamber.Online();
        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 上线/下线只改模块模式 Mode（不经设备），置位即成功；界面经事件流刷新模式灯。
    /// </summary>
    public Task<RpcResponse> OfflineAsync(string module)
    {
        var chamber = FindModule<BaseChamberModule>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        chamber.Offline();
        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 状态快照与事件流发布的是同一份 ChamberDto。
    /// </summary>
    public Task<RpcResponse> GetStateAsync(string module)
    {
        var dto = Roots.OfType<BaseChamberModule>()
            .Where(chamber => string.IsNullOrWhiteSpace(module)
                              || string.Equals(chamber.Name, module, StringComparison.OrdinalIgnoreCase))
            .Select(chamber => chamber.CreateStateDto())
            .ToList();

        var data = dto.Count == 1 ? JsonHelper.Serialize(dto[0]) : JsonHelper.Serialize(dto);
        return Task.FromResult(RpcResponse.Ok(data));
    }
}
