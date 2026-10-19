using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// LoadPort 服务给界面操作
/// </summary>
public class LoadPortService : BaseService, ILoadPortService
{
    public Task<RpcResponse> LoadAsync(string module)
    {
        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, port, port.Load(), port.LoadTimeout);
    }

    public Task<RpcResponse> UnloadAsync(string module)
    {
        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, port, port.UnloadManually(), port.UnloadTimeout);
    }

    public Task<RpcResponse> HomeAsync(string module)
    {
        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, port, port.Home(), port.HomeTimeout);
    }

    public Task<RpcResponse> ResetAsync(string module)
    {
        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, port, port.Reset(), port.CheckReset, port.ResetTimeout);
    }

    public Task<RpcResponse> AbortAsync(string module)
    {
        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunOperation(module, port, port.Abort(), port.CheckAbort, port.AbortTimeout);
    }


    public Task<RpcResponse> OnlineAsync(string module)
    {
        return Online<ILoadPort>(module);
    }

    public Task<RpcResponse> OfflineAsync(string module)
    {
        return Offline<ILoadPort>(module);
    }

    public Task<RpcResponse> AutoAsync(string module)
    {
        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        port.SetAutoMode(true);
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> ManualAsync(string module)
    {
        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        port.SetAutoMode(false);
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> ReadCarrierIdAsync(string module)
    {
        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return Task.FromResult(port._carrier.ReadId()
            ? RpcResponse.Ok()
            : RpcResponse.Fail(ErrorCodes.ReadCarrierIdRejected, [module]));
    }

    public Task<RpcResponse> GetStateAsync(string module)
    {
        var dto = IocHelper.GetServices<ILoadPort>()
            .Where(port => string.IsNullOrWhiteSpace(module)
                           || string.Equals(port.Name, module, StringComparison.OrdinalIgnoreCase))
            .Select(port => port.CreateStateDto())
            .ToList();

        var data = dto.Count == 1 ? JsonHelper.Serialize(dto[0]) : JsonHelper.Serialize(dto);
        return Task.FromResult(RpcResponse.Ok(data));
    }
}
