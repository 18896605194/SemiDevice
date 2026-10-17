using xyz.Components;
using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service;

/// <summary>
/// LoadPort 手动操作 gRPC 服务（命令通道）：下发 → 同步等终态 → 码+参数回包。
/// 公共流程（找模块、被拒/超时/终态回包）在 <see cref="BaseService"/>。
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

    /// <summary>
    /// 手动卸载：不扫图、不对账（SC AutoRunMapOnUnload 只管自动跑货的 Unload）——账对不对不拦手动这一下；
    /// 片没回齐、Job 在用照样拦（端口入口查，被拒原样回原因）。
    /// </summary>
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

    /// <summary>
    /// 上线/下线走模块基类（见 <see cref="BaseService.Online{TModule}"/>）。
    /// </summary>
    public Task<RpcResponse> OnlineAsync(string module)
    {
        return Online<ILoadPort>(module);
    }

    public Task<RpcResponse> OfflineAsync(string module)
    {
        return Offline<ILoadPort>(module);
    }

    /// <summary>
    /// Auto/Manual 是 LoadPort 的 Access Mode（内部模式位，不经设备协议），置位即成功；
    /// 界面经事件流刷新 AUTO/MANUAL 灯，E84 组件下一拍按它开关与搬运车的交接。
    /// </summary>
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

    /// <summary>
    /// Auto/Manual 是 LoadPort 的 Access Mode（内部模式位，不经设备协议），置位即成功；
    /// 界面经事件流刷新 AUTO/MANUAL 灯，E84 组件下一拍按它开关与搬运车的交接。
    /// </summary>
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

    /// <summary>
    /// 读码（读 _rfid）只发起不等结果：读头握手要几百毫秒，读到的 ID 随状态推送刷新；
    /// 读码失败的原因由模块记警告日志（客户端日志栏可见）。
    /// </summary>
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
        var dto = ComponentBase.FindAll<ILoadPort>()
            .Where(port => string.IsNullOrWhiteSpace(module)
                           || string.Equals(port.Name, module, StringComparison.OrdinalIgnoreCase))
            .Select(port => port.CreateStateDto())
            .ToList();

        var data = dto.Count == 1 ? JsonHelper.Serialize(dto[0]) : JsonHelper.Serialize(dto);
        return Task.FromResult(RpcResponse.Ok(data));
    }
}
