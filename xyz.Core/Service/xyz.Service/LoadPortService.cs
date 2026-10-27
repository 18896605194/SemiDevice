using xyz.Elements.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

public class LoadPortService : BaseService, ILoadPortService
{
    public Task<RpcResponse> LoadAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(port.Load(), port.LoadTimeout);
    }

    public Task<RpcResponse> UnloadAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(port.UnloadManually(), port.UnloadTimeout);
    }

    public Task<RpcResponse> HomeAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(port.Home(), port.HomeTimeout);
    }

    public Task<RpcResponse> ResetAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(port.Reset(), port.ResetTimeout);
    }

    public Task<RpcResponse> AbortAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        return RunRoutine(port.Abort(), port.AbortTimeout);
    }


    public Task<RpcResponse> SetProductionAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var port = FindModule<ILoadPort>(module) as IMode;
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        port.SetProduction();
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> SetMaintenanceAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var port = FindModule<ILoadPort>(module) as IMode;
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        port.SetMaintenance();
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> AutoAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        port.SetAutoMode(true);
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> ManualAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        port.SetAutoMode(false);
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> ReadCarrierIdAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var port = FindModule<ILoadPort>(module);
        if (port is null)
        {
            return ModuleNotFound(module);
        }

        if (!port._carrier.ReadId())
        {
            return Fail(ErrorCodes.ReadCarrierIdRejected, module);
        }

        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> GetStateAsync(RpcRequest request)
    {
        string module = request.Get(RpcParams.Module, string.Empty);
        var dto = IocHelper.GetServices<ILoadPort>()
            .Where(port => string.IsNullOrWhiteSpace(module)
                           || string.Equals(port.Name, module, StringComparison.OrdinalIgnoreCase))
            .Select(port => port.CreateStateDto())
            .ToList();

        var data = dto.Count == 1 ? JsonHelper.Serialize(dto[0]) : JsonHelper.Serialize(dto);
        return Task.FromResult(RpcResponse.Ok(data));
    }
}
