using System.Text.Json;
using ProtoBuf.Grpc;
using xyz.Common.Log;
using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;

namespace xyz.Service;

/// <summary>
/// LoadPort 手动操作 gRPC 服务（命令通道）：下发 → 同步等终态 → 码+参数回包。
/// </summary>
public class LoadPortService : ILoadPortService
{
    private readonly IReadOnlyList<ComponentBase> _roots;

    public LoadPortService(IReadOnlyList<ComponentBase> roots)
    {
        _roots = roots;
    }

    public Task<RpcResponse> LoadAsync(string module, CallContext context = default)
    {
        return Execute(module, port => port.Load(), port => port.LoadTimeout, context);
    }

    public Task<RpcResponse> UnloadAsync(string module, CallContext context = default)
    {
        return Execute(module, port => port.Unload(), port => port.UnloadTimeout, context);
    }

    public Task<RpcResponse> HomeAsync(string module, CallContext context = default)
    {
        return Execute(module, port => port.Home(), port => port.HomeTimeout, context);
    }

    public Task<RpcResponse> ResetAsync(string module, CallContext context = default)
    {
        return Execute(module, port => port.Reset(), port => port.ResetTimeout, context);
    }

    public Task<RpcResponse> AbortAsync(string module, CallContext context = default)
    {
        return Execute(module, port => port.Abort(), port => port.AbortTimeout, context);
    }

    /// <summary>
    /// 上线/下线是内部模式位（不经设备协议），置位即成功；界面经事件流刷新 AUTO/MANUAL 灯。
    /// </summary>
    public Task<RpcResponse> OnlineAsync(string module, CallContext context = default)
    {
        return SetModeAsync(module, true);
    }

    public Task<RpcResponse> OfflineAsync(string module, CallContext context = default)
    {
        return SetModeAsync(module, false);
    }

    public Task<RpcResponse> GetStateAsync(string module, CallContext context = default)
    {
        var dto = _roots.OfType<BaseLoadPortModule>()
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

        var data = dto.Count == 1 ? JsonSerializer.Serialize(dto[0]) : JsonSerializer.Serialize(dto);
        return Task.FromResult(RpcResponse.Ok(data));
    }

    /// <summary>
    /// 下发动作并同步等终态：模块不存在回 module.not_found，动作被拒回 module.action_rejected，
    /// 等待超时回 module.wait_timeout（只表示结果未确认，操作仍在执行，不能据此判失败或重发），
    /// 终态回 Ok 或操作自身的错误码。
    /// 请求已取消时不下发动作；等待期间取消只停止等待，不中止设备动作（需要时用 AbortAsync）。
    /// </summary>
    private Task<RpcResponse> Execute(string module,
        Func<BaseLoadPortModule, ModuleOperation?> start,
        Func<BaseLoadPortModule, int> timeoutOf,
        CallContext context)
    {
        var cancellationToken = context.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        var port = FindPort(module);
        if (port is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ModuleNotFound, [module]));
        }

        var operation = start(port);
        if (operation is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ActionRejected, [module, port.State.ToString()]));
        }

        var timeout = timeoutOf(port);
        LogHelper.Debug($"[LoadPort] {module} {operation.Name}: {operation.State}, Reason={operation.Reason}");

        if (!operation.WaitReply(timeout, cancellationToken))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaitTimeout,
                [operation.Name, timeout.ToString()]));
        }

        return Task.FromResult(operation.IsSuccess
            ? RpcResponse.Ok()
            : RpcResponse.Fail(operation.Code, operation.ErrorArgs));
    }

    private Task<RpcResponse> SetModeAsync(string module, bool autoMode)
    {
        var port = FindPort(module);
        if (port is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ModuleNotFound, [module]));
        }

        port.SetAutoMode(autoMode);
        return Task.FromResult(RpcResponse.Ok());
    }

    private BaseLoadPortModule? FindPort(string module)
    {
        return _roots.OfType<BaseLoadPortModule>()
            .FirstOrDefault(port => string.Equals(port.Name, module, StringComparison.OrdinalIgnoreCase));
    }
}
