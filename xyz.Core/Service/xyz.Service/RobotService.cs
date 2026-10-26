using System.Globalization;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

public class RobotService : BaseService, IRobotService
{
    private readonly ITransferManager? _transfers;

    public RobotService()
    {
        _transfers = IocHelper.GetService<ITransferManager>();
    }

    public Task<RpcResponse> HomeAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var robot = FindModule<IRobot>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(module, robot, robot.Home(), robot.HomeTimeout);
    }

    public Task<RpcResponse> ResetAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var robot = FindModule<IRobot>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(module, robot, robot.Reset(), robot.CheckReset, robot.ResetTimeout);
    }

    public Task<RpcResponse> AbortAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var robot = FindModule<IRobot>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(module, robot, robot.Abort(), robot.CheckAbort, robot.AbortTimeout);
    }

    /// <summary>
    /// 手动取片：站点这个槽 → 这台机械手的这只手。跟自动跑货一样交给搬运管理（只取片）：先准备站点（腔体开门、LoadPort 要 Load 着），
    /// 再让机械手取；手、槽、片都上锁，自动在跑时不会撞车。被拒、失败都回具体原因。
    /// </summary>
    public Task<RpcResponse> PickAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<int>(RpcParams.Arm, out var arm)
            || !request.TryGet<string>(RpcParams.Station, out var station)
            || !request.TryGet<int>(RpcParams.Slot, out var slot))
        {
            return InvalidRequest();
        }

        var robot = FindModule<IRobot>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunTransfer(new TransferRequest
        {
            Origin = TransferOrigin.Manual,
            Source = station,
            SourceSlot = slot,
            Target = robot.Name,
            TargetSlot = arm,
            Robot = robot.Name,
            Arm = arm,
        });
    }

    /// <summary>
    /// 手动放片：这台机械手这只手上的片 → 站点这个槽。同 <see cref="PickAsync"/>，交给搬运管理（只放片）。
    /// </summary>
    public Task<RpcResponse> PlaceAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module)
            || !request.TryGet<int>(RpcParams.Arm, out var arm)
            || !request.TryGet<string>(RpcParams.Station, out var station)
            || !request.TryGet<int>(RpcParams.Slot, out var slot))
        {
            return InvalidRequest();
        }

        var robot = FindModule<IRobot>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunTransfer(new TransferRequest
        {
            Origin = TransferOrigin.Manual,
            Source = robot.Name,
            SourceSlot = arm,
            Target = station,
            TargetSlot = slot,
            Robot = robot.Name,
            Arm = arm,
        });
    }

    /// <summary>
    /// 交给搬运管理跑这一趟、等它做完（等的上限跟手动传片一样，EC ManualWaitTimeoutMs；等不到界面先回超时，搬运照样跑完）。
    /// </summary>
    private async Task<RpcResponse> RunTransfer(TransferRequest request)
    {
        if (_transfers is null)
        {
            return await NotInstalled(ErrorCodes.TransferNotInstalled);
        }

        var started = _transfers.Start(request);
        var routine = started.Result;
        if (!started.IsSuccess || routine is null)
        {
            return RpcResponse.Fail(started.ErrorMessage, started.Args);
        }

        int timeout = _transfers.ManualWaitTimeoutMs;
        if (!await Task.Run(() => routine.WaitReply(timeout)).ConfigureAwait(false))
        {
            return RpcResponse.Fail(ErrorCodes.TransferWaitTimeout, [routine.Name, timeout.ToString(CultureInfo.InvariantCulture)]);
        }

        if (routine.IsSuccess)
        {
            return RpcResponse.Ok();
        }

        return Failed(routine);
    }

    public Task<RpcResponse> PowerOnAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var robot = FindModule<IRobot>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(module, robot, robot.PowerOn(), robot.PowerTimeout);
    }

    public Task<RpcResponse> PowerOffAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var robot = FindModule<IRobot>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(module, robot, robot.PowerOff(), robot.PowerTimeout);
    }

    /// <summary>
    /// 切生产 / 维护模式走服务基类（见 <see cref="BaseService.SetProduction{TModule}"/>）
    /// </summary>
    public Task<RpcResponse> SetProductionAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        return SetProduction<IRobot>(module);
    }

    public Task<RpcResponse> SetMaintenanceAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        return SetMaintenance<IRobot>(module);
    }

    /// <summary>
    /// 状态快照与事件流发布的是同一份 RobotDto。
    /// </summary>
    public Task<RpcResponse> GetStateAsync(RpcRequest request)
    {
        string module = request.Get(RpcParams.Module, string.Empty);
        var dto = IocHelper.GetServices<IRobot>()
            .Where(robot => string.IsNullOrWhiteSpace(module)
                            || string.Equals(robot.Name, module, StringComparison.OrdinalIgnoreCase))
            .Select(robot => robot.CreateStateDto())
            .ToList();

        var data = dto.Count == 1 ? JsonHelper.Serialize(dto[0]) : JsonHelper.Serialize(dto);
        return Task.FromResult(RpcResponse.Ok(data));
    }
}
