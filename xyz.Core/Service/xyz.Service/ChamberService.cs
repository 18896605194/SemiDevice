using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// 腔体手动操作 gRPC 服务（命令通道）：下发 → 同步等终态 → 码+参数回包。
/// 检查都在腔体的动作入口里（状态 → 资源 → 互锁），被拒照原样回入口给的原因；公共流程（找模块、被拒/超时/终态回包）在 <see cref="BaseService"/>。
/// </summary>
public class ChamberService : BaseService, IChamberService
{
    private readonly IRecipeComponent? _recipes;

    private readonly ILedgerManager? _wafers;

    private readonly IJobManager? _jobs;

    public ChamberService()
    {
        _recipes = IocHelper.GetService<IRecipeComponent>();
        _wafers = IocHelper.GetService<ILedgerManager>();
        _jobs = IocHelper.GetService<IJobManager>();
    }

    public Task<RpcResponse> HomeAsync(string module)
    {
        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, chamber, chamber.Home(), chamber.HomeTimeout);
    }

    public Task<RpcResponse> ResetAsync(string module)
    {
        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, chamber, chamber.Reset(), chamber.CheckReset, chamber.ResetTimeout);
    }

    public Task<RpcResponse> AbortAsync(string module)
    {
        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, chamber, chamber.Abort(), chamber.CheckAbort, chamber.AbortTimeout);
    }

    public Task<RpcResponse> ProcessAsync(ChamberProcessRequest request)
    {
        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        var module = request.Module ?? string.Empty;
        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        var recipe = (request.Recipe ?? string.Empty).Trim();
        if (recipe.Length == 0)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.RecipeRequired, [module]));
        }

        // 配方要在工艺配方库里：被删了、改名了的配方不让起（没装库只有名字，腔体会回拿不到配方步骤）；
        // 起工艺带的是这一刻的配方快照，跑的过程中库里改了也不影响这一次
        RecipeData? snapshot = null;
        if (_recipes is not null)
        {
            snapshot = _recipes.Find(recipe);
            if (snapshot is null)
            {
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberRecipeNotFound, [module, recipe]));
            }
        }

        // 腔里的片正在 Job 里：工艺归 Job 起，手动不能插一脚（只拦手动、不拦 Job 自己，所以留在发命令这一层）
        var wafer = _wafers?.Get(chamber.Name, 1);
        if (wafer is not null)
        {
            string? owner = _jobs?.OwnerOf(wafer.Id);
            if (owner is not null)
            {
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberWaferOwned, [module, wafer.WaferId, owner]));
            }
        }

        // 配方对不对得上这个腔体（Type、选的摆臂和药液）、腔体此刻能不能起，都由腔体入口自己查；
        // 起了就等到做完为止，上限跟工艺操作一样是配方头的超时时间
        var process = new ProcessRequest { Origin = ProcessOrigin.Manual, RecipeName = recipe, Recipe = snapshot };
        int timeout = snapshot is null ? 0 : snapshot.TimeoutMilliseconds;
        return RunOperation(module, chamber, chamber.StartProcess(process), timeout);
    }

    #region 设备手动动作（轴、气缸按设备推送里的 Path 找）

    public Task<RpcResponse> AxisHomeAsync(ChamberDeviceRequest request)
    {
        return RunDeviceAction(request.Module, chamber => chamber.AxisHome(request.Device ?? string.Empty));
    }

    public Task<RpcResponse> AxisMoveAsync(ChamberAxisMoveRequest request)
    {
        return RunDeviceAction(request.Module, chamber => chamber.AxisMove(request.Axis ?? string.Empty, request.Position, request.Speed));
    }

    public Task<RpcResponse> AxisStepAsync(ChamberAxisStepRequest request)
    {
        return RunDeviceAction(request.Module, chamber => chamber.AxisStep(request.Axis ?? string.Empty, request.Distance, request.Speed));
    }

    public Task<RpcResponse> AxisResetAsync(ChamberDeviceRequest request)
    {
        return RunDeviceAction(request.Module, chamber => chamber.AxisReset(request.Device ?? string.Empty));
    }

    public Task<RpcResponse> CylinderUpAsync(ChamberDeviceRequest request)
    {
        return RunDeviceAction(request.Module, chamber => chamber.MoveCylinder(request.Device ?? string.Empty, true));
    }

    public Task<RpcResponse> CylinderDownAsync(ChamberDeviceRequest request)
    {
        return RunDeviceAction(request.Module, chamber => chamber.MoveCylinder(request.Device ?? string.Empty, false));
    }

    /// <summary>
    /// 点动：发起了就回 Ok（不等），之后按住期间续、松手发停止
    /// </summary>
    public Task<RpcResponse> AxisJogAsync(ChamberAxisJogRequest request)
    {
        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        var module = request.Module ?? string.Empty;
        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        var started = chamber.AxisJog(request.Axis ?? string.Empty, request.Speed);
        if (!started.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(started.ErrorMessage, [.. started.Args]));
        }

        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 停止：腔体正忙也照发，发出去就回 Ok
    /// </summary>
    public Task<RpcResponse> AxisStopAsync(ChamberDeviceRequest request)
    {
        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        var module = request.Module ?? string.Empty;
        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        var sent = chamber.AxisStop(request.Device ?? string.Empty);
        if (!sent.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(sent.ErrorMessage, [.. sent.Args]));
        }

        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 续点动：正按着的就是这根轴回 Ok，否则回 chamber.jog_not_held（界面据此不用再续）。
    /// </summary>
    public Task<RpcResponse> AxisJogRenewAsync(ChamberDeviceRequest request)
    {
        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        var module = request.Module ?? string.Empty;
        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        var path = request.Device ?? string.Empty;
        return Task.FromResult(chamber.AxisJogRenew(path)
            ? RpcResponse.Ok()
            : RpcResponse.Fail(ErrorCodes.ChamberJogNotHeld, [path, nameof(ChamberDeviceAction.Jog)]));
    }

    /// <summary>
    /// 发设备动作并回包：被拒（找不到设备、参数不对、指令没发出去、状态不允许、已有动作在途）照原样回入口给的错误码 + 参数；
    /// 发起了就同步等设备做完（上限 EC DeviceActionTimeout）
    /// </summary>
    private Task<RpcResponse> RunDeviceAction(string? module, Func<IChamber, HandleResult<ModuleOperation>> start)
    {
        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        module ??= string.Empty;
        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, chamber, start(chamber), chamber.DeviceActionTimeout);
    }

    #endregion

    /// <summary>
    /// 上线/下线走模块基类（见 <see cref="BaseService.Online{TModule}"/>）。
    /// </summary>
    public Task<RpcResponse> OnlineAsync(string module)
    {
        return Online<IChamber>(module);
    }

    public Task<RpcResponse> OfflineAsync(string module)
    {
        return Offline<IChamber>(module);
    }

    /// <summary>
    /// 状态快照与事件流发布的是同一份 ChamberDto。
    /// </summary>
    public Task<RpcResponse> GetStateAsync(string module)
    {
        var dto = IocHelper.GetServices<IChamber>()
            .Where(chamber => string.IsNullOrWhiteSpace(module)
                              || string.Equals(chamber.Name, module, StringComparison.OrdinalIgnoreCase))
            .Select(chamber => chamber.CreateStateDto())
            .ToList();

        var data = dto.Count == 1 ? JsonHelper.Serialize(dto[0]) : JsonHelper.Serialize(dto);
        return Task.FromResult(RpcResponse.Ok(data));
    }
}
