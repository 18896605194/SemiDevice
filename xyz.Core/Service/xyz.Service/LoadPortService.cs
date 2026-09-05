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
        var port = FindPort(module);
        if (port is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ModuleNotFound, [module]));
        }

        var operation = port.Load();
        if (operation is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ActionRejected, [module, port.State.ToString()]));
        }

        LogHelper.Debug($"[LoadPort] {module} Load: {operation.State}, Reason={operation.Reason}");
        operation.Wait(port.LoadTimeout);

        if (operation.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Ok());
        }

        return Task.FromResult(RpcResponse.Fail(operation.Code, operation.ErrorArgs));
    }

    public Task<RpcResponse> UnloadAsync(string module, CallContext context = default)
    {
        var port = FindPort(module);
        if (port is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ModuleNotFound, [module]));
        }

        var operation = port.Unload();
        if (operation is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ActionRejected, [module, port.State.ToString()]));
        }

        LogHelper.Debug($"[LoadPort] {module} Unload: {operation.State}, Reason={operation.Reason}");
        operation.Wait(port.UnloadTimeout);

        if (operation.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Ok());
        }

        return Task.FromResult(RpcResponse.Fail(operation.Code, operation.ErrorArgs));
    }

    public Task<RpcResponse> HomeAsync(string module, CallContext context = default)
    {
        var port = FindPort(module);
        if (port is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ModuleNotFound, [module]));
        }

        var operation = port.Home();
        if (operation is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ActionRejected, [module, port.State.ToString()]));
        }

        LogHelper.Debug($"[LoadPort] {module} Home: {operation.State}, Reason={operation.Reason}");
        operation.Wait(port.HomeTimeout);

        if (operation.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Ok());
        }

        return Task.FromResult(RpcResponse.Fail(operation.Code, operation.ErrorArgs));
    }

    public Task<RpcResponse> ResetAsync(string module, CallContext context = default)
    {
        var port = FindPort(module);
        if (port is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ModuleNotFound, [module]));
        }

        var operation = port.Reset();
        if (operation is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ActionRejected, [module, port.State.ToString()]));
        }

        LogHelper.Debug($"[LoadPort] {module} Reset: {operation.State}, Reason={operation.Reason}");
        operation.Wait(port.ResetTimeout);

        if (operation.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Ok());
        }

        return Task.FromResult(RpcResponse.Fail(operation.Code, operation.ErrorArgs));
    }

    public Task<RpcResponse> AbortAsync(string module, CallContext context = default)
    {
        var port = FindPort(module);
        if (port is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ModuleNotFound, [module]));
        }

        var operation = port.Abort();
        if (operation is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ActionRejected, [module, port.State.ToString()]));
        }

        LogHelper.Debug($"[LoadPort] {module} Abort: {operation.State}, Reason={operation.Reason}");
        operation.Wait(port.AbortTimeout);

        if (operation.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Ok());
        }

        return Task.FromResult(RpcResponse.Fail(operation.Code, operation.ErrorArgs));
    }

    public Task<RpcResponse> GetStateAsync(string module, CallContext context = default)
    {
        var dto = _roots.OfType<LoadPortBase>()
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

    private LoadPortBase? FindPort(string module)
    {
        return _roots.OfType<LoadPortBase>()
            .FirstOrDefault(port => string.Equals(port.Name, module, StringComparison.OrdinalIgnoreCase));
    }
}
