using xyz.Elements.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;

namespace xyz.Service;

/// <summary>
/// 供液操作服务
/// </summary>
public class SupplyService : BaseService, ISupplyService
{
    public Task<RpcResponse> SetProductionAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var supply = FindSupply(module);
        if (supply is null)
        {
            return ModuleNotFound(module);
        }

        supply.SetProduction();
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> SetMaintenanceAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module))
        {
            return InvalidRequest();
        }

        var supply = FindSupply(module);
        if (supply is null)
        {
            return ModuleNotFound(module);
        }

        supply.SetMaintenance();
        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 按名字找供液模块：ISupplyModule 不继承 IModule，先按模块名找、再看是不是供液；不是或者找不到为 null
    /// </summary>
    private static IMode? FindSupply(string name)
    {
        if (FindModule<IModule>(name) is ISupplyModule supply)
        {
            return supply as IMode;
        }

        return null;
    }
}
