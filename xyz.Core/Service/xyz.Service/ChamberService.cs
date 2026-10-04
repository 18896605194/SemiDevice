using xyz.Components;
using xyz.Modules;
using xyz.Modules.Enums;
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

        var recipe = (request.Recipe ?? string.Empty).Trim();
        if (recipe.Length == 0)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.RecipeRequired, [module]));
        }

        // 配方要在工艺配方库里（没装库就不查）：被删了、改名了的配方不让起；
        // 配方里下拉选的（摆臂、药液这类从腔体部件取的）这个腔体也得有——几个腔体装的不一样时，别的腔体的配方起不了
        var library = ProcessRecipeComponent.Current;
        if (library is not null && !library.Contains(recipe))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberRecipeNotFound, [module, recipe]));
        }

        var mismatch = library?.FindMismatch(recipe, module);
        if (mismatch is not null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberRecipeOptionMissing, [module, recipe, mismatch.Field, mismatch.Value]));
        }

        return RunOperation(module, chamber, chamber.Process(recipe), chamber.ProcessTimeout);
    }

    /// <summary>
    /// 部件手动动作：找不到部件、没有这个动作、参数不对、指令没发出去各回各的码；状态不允许或已有动作在途走 module.action_rejected；
    /// 普通动作发起了就同步等部件做完（上限 EC PartActionTimeout）；停止类发出去、按住类发起了就回。
    /// </summary>
    public Task<RpcResponse> PartActionAsync(PartActionRequest request)
    {
        // protobuf 传输省略默认值字段，空字符串、空列表在接收端可能为 null。
        var module = request.Module ?? string.Empty;
        var chamber = FindModule<BaseChamberModule>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        var path = request.Part ?? string.Empty;
        var action = request.Action ?? string.Empty;
        IReadOnlyList<string> args = request.Args ?? [];
        switch (chamber.TryPartAction(path, action, args, out var operation))
        {
            case ChamberPartActionResult.NotFound:
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberPartNotFound, [module, path]));

            case ChamberPartActionResult.Unsupported:
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberPartActionUnsupported, [path, action]));

            case ChamberPartActionResult.InvalidArgs:
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberPartActionArgsInvalid, [path, action]));

            case ChamberPartActionResult.CommandRejected:
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberPartCommandRejected, [path, action]));

            case ChamberPartActionResult.Sent:
            case ChamberPartActionResult.Holding:
                return Task.FromResult(RpcResponse.Ok());

            default:
                return RunOperation(module, chamber, operation, chamber.PartActionTimeout);
        }
    }

    /// <summary>
    /// 续按住类动作（点动）：正按着的就是这个动作回 Ok，否则回 chamber.part_not_held（界面据此不用再续）。
    /// </summary>
    public Task<RpcResponse> RenewPartActionAsync(PartActionRequest request)
    {
        var module = request.Module ?? string.Empty;
        var chamber = FindModule<BaseChamberModule>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        var path = request.Part ?? string.Empty;
        var action = request.Action ?? string.Empty;
        return Task.FromResult(chamber.RenewPartAction(path, action)
            ? RpcResponse.Ok()
            : RpcResponse.Fail(ErrorCodes.ChamberPartNotHeld, [path, action]));
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
