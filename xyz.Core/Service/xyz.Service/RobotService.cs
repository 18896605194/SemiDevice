using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// Robot 手动操作 gRPC 服务（命令通道）：下发 → 同步等终态 → 码+参数回包。
/// 公共流程（找模块、被拒/超时/终态回包）在 <see cref="BaseService"/>。
/// </summary>
public class RobotService : BaseService, IRobotService
{
    public RobotService(IReadOnlyList<ComponentBase> roots) : base(roots)
    {
    }

    public Task<RpcResponse> HomeAsync(string module)
    {
        var robot = FindModule<BaseRobotModule>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, robot, robot.Home(), robot.HomeTimeout);
    }

    public Task<RpcResponse> ResetAsync(string module)
    {
        var robot = FindModule<BaseRobotModule>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, robot, robot.Reset(), robot.ResetTimeout);
    }

    public Task<RpcResponse> AbortAsync(string module)
    {
        var robot = FindModule<BaseRobotModule>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, robot, robot.Abort(), robot.AbortTimeout);
    }

    public Task<RpcResponse> PickAsync(RobotPickPlaceRequest request)
    {
        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        var module = request.Module ?? string.Empty;
        var robot = FindModule<BaseRobotModule>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        var station = request.Station ?? string.Empty;
        if (!robot.TryGetStation(station, out _))
        {
            return StationNotFound(module, station);
        }

        return RunOperation(module, robot, robot.Pick(request.Arm, station, request.Slot), robot.PickTimeout);
    }

    public Task<RpcResponse> PlaceAsync(RobotPickPlaceRequest request)
    {
        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        var module = request.Module ?? string.Empty;
        var robot = FindModule<BaseRobotModule>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        var station = request.Station ?? string.Empty;
        if (!robot.TryGetStation(station, out _))
        {
            return StationNotFound(module, station);
        }

        return RunOperation(module, robot, robot.Place(request.Arm, station, request.Slot), robot.PlaceTimeout);
    }

    public Task<RpcResponse> PowerOnAsync(string module)
    {
        var robot = FindModule<BaseRobotModule>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, robot, robot.PowerOn(), robot.PowerTimeout);
    }

    public Task<RpcResponse> PowerOffAsync(string module)
    {
        var robot = FindModule<BaseRobotModule>(module);
        if (robot is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, robot, robot.PowerOff(), robot.PowerTimeout);
    }

    /// <summary>
    /// 状态快照与事件流发布的是同一份 RobotDto。
    /// </summary>
    public Task<RpcResponse> GetStateAsync(string module)
    {
        var dto = Roots.OfType<BaseRobotModule>()
            .Where(robot => string.IsNullOrWhiteSpace(module)
                            || string.Equals(robot.Name, module, StringComparison.OrdinalIgnoreCase))
            .Select(robot => robot.CreateStateDto())
            .ToList();

        var data = dto.Count == 1 ? JsonHelper.Serialize(dto[0]) : JsonHelper.Serialize(dto);
        return Task.FromResult(RpcResponse.Ok(data));
    }

    /// <summary>
    /// 站点未在该机械手站点表中配置回包（robot.station_not_found）。
    /// </summary>
    private static Task<RpcResponse> StationNotFound(string module, string station)
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.StationNotFound, [module, station]));
    }
}
