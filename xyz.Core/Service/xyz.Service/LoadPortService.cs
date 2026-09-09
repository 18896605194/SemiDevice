using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// LoadPort 手动操作 gRPC 服务（命令通道）：下发 → 同步等终态 → 码+参数回包。
/// 公共流程（找模块、被拒/超时/终态回包）在 <see cref="BaseService"/>。
/// </summary>
public class LoadPortService : BaseService, ILoadPortService
{
    public LoadPortService(IReadOnlyList<ComponentBase> roots) : base(roots)
    {
    }

    public Task<RpcResponse> LoadAsync(string module)
    {
        var port = FindModule<BaseLoadPortModule>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, port, port.Load(), port.LoadTimeout);
    }

    public Task<RpcResponse> UnloadAsync(string module)
    {
        var port = FindModule<BaseLoadPortModule>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, port, port.Unload(), port.UnloadTimeout);
    }

    public Task<RpcResponse> HomeAsync(string module)
    {
        var port = FindModule<BaseLoadPortModule>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, port, port.Home(), port.HomeTimeout);
    }

    public Task<RpcResponse> ResetAsync(string module)
    {
        var port = FindModule<BaseLoadPortModule>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, port, port.Reset(), port.ResetTimeout);
    }

    public Task<RpcResponse> AbortAsync(string module)
    {
        var port = FindModule<BaseLoadPortModule>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, port, port.Abort(), port.AbortTimeout);
    }

    /// <summary>
    /// 上线/下线是内部模式位（不经设备协议），置位即成功；界面经事件流刷新 AUTO/MANUAL 灯。
    /// </summary>
    public Task<RpcResponse> OnlineAsync(string module)
    {
        var port = FindModule<BaseLoadPortModule>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        port.SetAutoMode(true);
        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 上线/下线是内部模式位（不经设备协议），置位即成功；界面经事件流刷新 AUTO/MANUAL 灯。
    /// </summary>
    public Task<RpcResponse> OfflineAsync(string module)
    {
        var port = FindModule<BaseLoadPortModule>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        port.SetAutoMode(false);
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> GetStateAsync(string module)
    {
        var dto = Roots.OfType<BaseLoadPortModule>()
            .Where(port => string.IsNullOrWhiteSpace(module)
                           || string.Equals(port.Name, module, StringComparison.OrdinalIgnoreCase))
            .Select(port => new LoadPortDto
            {
                Name = port.Name,
                State = port.State,
                IsConnected = port.Driver?.IsConnected ?? false,
                IsPodPlaced = port.IsPodPlaced,
            })
            .ToList();

        var data = dto.Count == 1 ? JsonHelper.Serialize(dto[0]) : JsonHelper.Serialize(dto);
        return Task.FromResult(RpcResponse.Ok(data));
    }
}
