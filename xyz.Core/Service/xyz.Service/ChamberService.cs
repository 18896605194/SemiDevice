using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// 腔体操作服务
/// </summary>
public class ChamberService : BaseService, IChamberService
{
    private readonly IRecipeElement? _recipes;
    private readonly ILedgerManager? _wafers;
    private readonly IJobManager? _jobs;

    public ChamberService()
    {
        _recipes = IocHelper.GetService<IRecipeElement>();
        _wafers = IocHelper.GetService<ILedgerManager>();
        _jobs = IocHelper.GetService<IJobManager>();
    }

    public Task<RpcResponse> HomeAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(chamber.Home(), chamber.HomeTimeout);
    }

    public Task<RpcResponse> ResetAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(chamber.Reset(), chamber.ResetTimeout);
    }

    public Task<RpcResponse> AbortAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(chamber.Abort(), chamber.AbortTimeout);
    }

    public Task<RpcResponse> ProcessAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<string>(RpcParams.Recipe, out var recipe))
        {
            return InvalidRequest();
        }

        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        recipe = recipe.Trim();
        if (recipe.Length == 0)
        {
            return Fail(ErrorCodes.RecipeRequired, module);
        }

        // 配方要在工艺配方库里：被删了、改名了的配方不让起（没装库只有名字，腔体会回拿不到配方步骤）；
        // 起工艺带的是这一刻的配方快照，跑的过程中库里改了也不影响这一次
        RecipeData? snapshot = null;
        if (_recipes is not null)
        {
            snapshot = _recipes.Find(recipe);
            if (snapshot is null)
            {
                return Fail(ErrorCodes.ChamberRecipeNotFound, module, recipe);
            }
        }

        // 腔里的片正在 Job 里：工艺归 Job 起，手动不能插一脚（只拦手动、不拦 Job 自己，所以留在发命令这一层）
        var wafer = _wafers?.Get(chamber.Name, 1);
        if (wafer is not null)
        {
            string? owner = _jobs?.OwnerOf(wafer.Id);
            if (owner is not null)
            {
                return Fail(ErrorCodes.ChamberWaferOwned, module, wafer.WaferId, owner);
            }
        }

        // 配方对不对得上这个腔体（Type、选的摆臂和药液）、腔体此刻能不能起，都由腔体入口自己查；
        // 起了就等到做完为止，上限跟工艺动作一样是配方头的超时时间
        var process = new ProcessRequest { Origin = ProcessOrigin.Manual, RecipeName = recipe, Recipe = snapshot };
        int timeout = snapshot is null ? 0 : snapshot.TimeoutMilliseconds;
        return RunRoutine(chamber.StartProcess(process), timeout);
    }

    #region 设备手动动作（轴、气缸按设备推送里的 Path 找）

    public Task<RpcResponse> AxisHomeAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<string>(RpcParams.Device, out var device))
        {
            return InvalidRequest();
        }

        return RunDeviceAction(module, chamber => chamber.AxisHome(device));
    }

    public Task<RpcResponse> AxisMoveAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<string>(RpcParams.Axis, out var axis)
            || !request.TryGet<double>(RpcParams.Position, out var position))
        {
            return InvalidRequest();
        }

        double speed = request.Get(RpcParams.Speed, 0.0);
        return RunDeviceAction(module, chamber => chamber.AxisMove(axis, position, speed));
    }

    public Task<RpcResponse> AxisStepAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<string>(RpcParams.Axis, out var axis)
            || !request.TryGet<double>(RpcParams.Distance, out var distance))
        {
            return InvalidRequest();
        }

        double speed = request.Get(RpcParams.Speed, 0.0);
        return RunDeviceAction(module, chamber => chamber.AxisStep(axis, distance, speed));
    }

    public Task<RpcResponse> AxisResetAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<string>(RpcParams.Device, out var device))
        {
            return InvalidRequest();
        }

        return RunDeviceAction(module, chamber => chamber.AxisReset(device));
    }

    public Task<RpcResponse> CylinderUpAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<string>(RpcParams.Device, out var device))
        {
            return InvalidRequest();
        }

        return RunDeviceAction(module, chamber => chamber.MoveCylinder(device, true));
    }

    public Task<RpcResponse> CylinderDownAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<string>(RpcParams.Device, out var device))
        {
            return InvalidRequest();
        }

        return RunDeviceAction(module, chamber => chamber.MoveCylinder(device, false));
    }

    /// <summary>
    /// 点动：发起了就回 Ok（不等），之后按住期间续、松手发停止
    /// </summary>
    public Task<RpcResponse> AxisJogAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<string>(RpcParams.Axis, out var axis)
            || !request.TryGet<double>(RpcParams.Speed, out var speed))
        {
            return InvalidRequest();
        }

        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return Reply(chamber.AxisJog(axis, speed));
    }

    /// <summary>
    /// 停止：腔体正忙也照发，发出去就回 Ok
    /// </summary>
    public Task<RpcResponse> AxisStopAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<string>(RpcParams.Device, out var device))
        {
            return InvalidRequest();
        }

        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return Reply(chamber.AxisStop(device));
    }

    /// <summary>
    /// 发设备动作并回包：被拒（找不到设备、参数不对、指令没发出去、状态不允许、已有动作在途）照原样回入口给的错误码 + 参数；
    /// 发起了就同步等设备做完（上限 EC DeviceActionTimeout）
    /// </summary>
    private Task<RpcResponse> RunDeviceAction(string module, Func<IChamber, HandleResult<ModuleRoutine>> start)
    {
        var chamber = FindModule<IChamber>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(start(chamber), chamber.DeviceActionTimeout);
    }

    #endregion

    public Task<RpcResponse> SetProductionAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var chamber = FindModule<IChamber>(module) as IMode;
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        chamber.SetProduction();
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> SetMaintenanceAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var chamber = FindModule<IChamber>(module) as IMode;
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        chamber.SetMaintenance();
        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 状态快照与事件流发布的是同一份 ChamberDto。
    /// </summary>
    public Task<RpcResponse> GetStateAsync(RpcRequest request)
    {
        string module = request.Get(RpcParams.Module, string.Empty);
        var dto = IocHelper.GetServices<IChamber>()
            .Where(chamber => string.IsNullOrWhiteSpace(module)
                              || string.Equals(chamber.Name, module, StringComparison.OrdinalIgnoreCase))
            .Select(chamber => chamber.CreateStateDto())
            .ToList();

        var data = dto.Count == 1 ? JsonHelper.Serialize(dto[0]) : JsonHelper.Serialize(dto);
        return Task.FromResult(RpcResponse.Ok(data));
    }
}
