using xyz.Components;
using xyz.Components.Components;
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

        // 配方要在工艺配方库里（没装库就不查、只认名字）：被删了、改名了的配方不让起；
        // 起工艺带的是这一刻的配方快照，跑的过程中库里改了也不影响这一次
        var library = ProcessRecipeComponent.Current;
        ProcessRecipeData? snapshot = null;
        if (library is not null)
        {
            snapshot = library.Find(recipe);
            if (snapshot is null)
            {
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberRecipeNotFound, [module, recipe]));
            }
        }

        // 腔里的片正在 Job 里：工艺归 Job 起，手动不能插一脚
        var wafer = WaferManagerComponent.Current?.Get(chamber.Name, 1);
        string? owner = wafer is null ? null : JobManager.Current?.OwnerOf(wafer.Id);
        if (wafer is not null && owner is not null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberWaferOwned, [module, wafer.WaferId, owner]));
        }

        // 配方对不对得上这个腔体（摆臂、药液这类从腔体设备取的下拉）、腔体此刻能不能起，都由腔体自己查
        var process = new ProcessRequest { Origin = ProcessOrigin.Manual, RecipeName = recipe, Recipe = snapshot };
        var rejection = chamber.CheckProcess(process);
        if (rejection is not null)
        {
            return Task.FromResult(RpcResponse.Fail(rejection.Code, rejection.Args));
        }

        return RunOperation(module, chamber, chamber.StartProcess(process), chamber.ProcessTimeout);
    }

    #region 设备手动动作（轴、气缸按设备推送里的 Path 找）

    public Task<RpcResponse> AxisHomeAsync(ChamberDeviceRequest request)
    {
        return RunDeviceAction(request.Module, request.Device, ChamberDeviceAction.Home,
            (BaseChamberModule chamber, out ModuleOperation? operation) => chamber.AxisHome(request.Device ?? string.Empty, out operation));
    }

    public Task<RpcResponse> AxisMoveAsync(ChamberAxisMoveRequest request)
    {
        return RunDeviceAction(request.Module, request.Axis, ChamberDeviceAction.Move,
            (BaseChamberModule chamber, out ModuleOperation? operation) =>
                chamber.AxisMove(request.Axis ?? string.Empty, request.Position, request.Speed, out operation));
    }

    public Task<RpcResponse> AxisStepAsync(ChamberAxisStepRequest request)
    {
        return RunDeviceAction(request.Module, request.Axis, ChamberDeviceAction.Step,
            (BaseChamberModule chamber, out ModuleOperation? operation) =>
                chamber.AxisStep(request.Axis ?? string.Empty, request.Distance, request.Speed, out operation));
    }

    public Task<RpcResponse> AxisResetAsync(ChamberDeviceRequest request)
    {
        return RunDeviceAction(request.Module, request.Device, ChamberDeviceAction.Reset,
            (BaseChamberModule chamber, out ModuleOperation? operation) => chamber.AxisReset(request.Device ?? string.Empty, out operation));
    }

    public Task<RpcResponse> CylinderUpAsync(ChamberDeviceRequest request)
    {
        return RunDeviceAction(request.Module, request.Device, ChamberDeviceAction.Up,
            (BaseChamberModule chamber, out ModuleOperation? operation) => chamber.MoveCylinder(request.Device ?? string.Empty, true, out operation));
    }

    public Task<RpcResponse> CylinderDownAsync(ChamberDeviceRequest request)
    {
        return RunDeviceAction(request.Module, request.Device, ChamberDeviceAction.Down,
            (BaseChamberModule chamber, out ModuleOperation? operation) => chamber.MoveCylinder(request.Device ?? string.Empty, false, out operation));
    }

    /// <summary>点动：发起了就回 Ok，之后按住期间续、松手发停止。</summary>
    public Task<RpcResponse> AxisJogAsync(ChamberAxisJogRequest request)
    {
        return RunDeviceAction(request.Module, request.Axis, ChamberDeviceAction.Jog,
            (BaseChamberModule chamber, out ModuleOperation? operation) =>
            {
                operation = null;
                return chamber.AxisJog(request.Axis ?? string.Empty, request.Speed);
            });
    }

    /// <summary>停止：腔体正忙也照发，发出去就回 Ok。</summary>
    public Task<RpcResponse> AxisStopAsync(ChamberDeviceRequest request)
    {
        return RunDeviceAction(request.Module, request.Device, ChamberDeviceAction.Stop,
            (BaseChamberModule chamber, out ModuleOperation? operation) =>
            {
                operation = null;
                return chamber.AxisStop(request.Device ?? string.Empty);
            });
    }

    /// <summary>
    /// 续点动：正按着的就是这根轴回 Ok，否则回 chamber.jog_not_held（界面据此不用再续）。
    /// </summary>
    public Task<RpcResponse> AxisJogRenewAsync(ChamberDeviceRequest request)
    {
        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        var module = request.Module ?? string.Empty;
        var chamber = FindModule<BaseChamberModule>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        var path = request.Device ?? string.Empty;
        return Task.FromResult(chamber.AxisJogRenew(path)
            ? RpcResponse.Ok()
            : RpcResponse.Fail(ErrorCodes.ChamberJogNotHeld, [path, nameof(ChamberDeviceAction.Jog)]));
    }

    /// <summary>发起设备动作：out 是挂上的操作（停止、点动没有）。</summary>
    private delegate ChamberDeviceActionResult StartAction(BaseChamberModule chamber, out ModuleOperation? operation);

    /// <summary>
    /// 发设备动作并回包：找不到设备、参数不对、指令没发出去各回各的码；状态不允许或已有动作在途走 module.action_rejected；
    /// 普通动作发起了就同步等设备做完（上限 EC DeviceActionTimeout）；停止发出去、点动发起了就回。
    /// </summary>
    private Task<RpcResponse> RunDeviceAction(string? module, string? part, ChamberDeviceAction action, StartAction start)
    {
        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        module ??= string.Empty;
        part ??= string.Empty;
        var chamber = FindModule<BaseChamberModule>(module);
        if (chamber is null)
        {
            return ModuleNotFound(module);
        }

        string name = action.ToString();
        switch (start(chamber, out var operation))
        {
            case ChamberDeviceActionResult.NotFound:
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberDeviceNotFound, [module, part]));

            case ChamberDeviceActionResult.InvalidArgs:
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberDeviceArgsInvalid, [part, name]));

            case ChamberDeviceActionResult.CommandRejected:
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.ChamberDeviceCommandRejected, [part, name]));

            case ChamberDeviceActionResult.Sent:
            case ChamberDeviceActionResult.Holding:
                return Task.FromResult(RpcResponse.Ok());

            default:
                return RunOperation(module, chamber, operation, chamber.DeviceActionTimeout);
        }
    }

    #endregion

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
