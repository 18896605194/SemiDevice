using ProtoBuf.Grpc;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Systems;

/// <summary>
/// EC服务
/// </summary>
public class EcService : BaseService, IEcService
{
    private readonly IEc? _ec;

    public EcService()
    {
        _ec = IocHelper.GetService<IEc>();
    }

    public Task<RpcResponse> GetDefinitionsAsync(RpcRequest request, CallContext context = default)
    {
        var dtos = new List<EcItemDto>();
        if (_ec is not null)
        {
            dtos = _ec.Snapshot().Select(item => _ec.CreateDto(item.Path, item.Value)).ToList();
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dtos)));
    }

    public Task<RpcResponse> SetValueAsync(RpcRequest request, CallContext context = default)
    {
        if (_ec is null)
        {
            return NotInstalled(ErrorCodes.EcNotInstalled);
        }

        if (!request.TryGet<string>(RpcParams.Key, out var key)
            || !request.TryGet<string>(RpcParams.Value, out var value))
        {
            return InvalidRequest();
        }

        // 键是"组件全路径.参数名"：最后一个点前面是组件，后面是参数。
        key = key.Trim();
        int dot = key.LastIndexOf('.');
        if (dot <= 0 || dot == key.Length - 1)
        {
            return Fail(ErrorCodes.EcNotFound, key);
        }

        string path = key[..dot];
        string name = key[(dot + 1)..];
        string old = _ec.Get(path, name);
        var result = _ec.TrySet(path, name, value, out var item);
        if (item is null)
        {
            return Fail(ErrorCodes.EcNotFound, key);
        }

        switch (result)
        {
            case EcSetResult.InvalidFormat:
                return Fail(ErrorCodes.EcInvalidFormat, key, item.Format ?? string.Empty);
            case EcSetResult.OutOfRange:
                return Fail(ErrorCodes.EcOutOfRange, key, item.Min ?? string.Empty, item.Max ?? string.Empty, item.Unit ?? string.Empty);
            case EcSetResult.InvalidOption:
                return Fail(ErrorCodes.EcInvalidOption, key, item.Options ?? string.Empty);
            case EcSetResult.SaveFailed:
                return Fail(ErrorCodes.EcSaveFailed, key);
            case EcSetResult.Ok:
                LogChange($"界面改 EC {key}：{old} → {item.Value} {item.Unit}".TrimEnd());
                break;
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(_ec.CreateDto(path, item))));
    }

    /// <summary>
    /// 改 EC 的记录（带改之前的值，操作日志里没有）记在 EC 组件名下
    /// </summary>
    private void LogChange(string message)
    {
        if (_ec is ILogSource source)
        {
            LogHelper.Info(source, message);
            return;
        }

        LogHelper.Info(LogHelper.HostModule, message);
    }
}
