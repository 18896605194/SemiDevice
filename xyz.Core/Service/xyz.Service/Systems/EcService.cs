using ProtoBuf.Grpc;
using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Systems;

/// <summary>
/// EC gRPC 服务：把 EC 组件里的全部参数（定义 + 当前值）给界面，界面改值也走这儿——
/// EC 设置页列参数、改值，通用输入框按 EcKey 取范围，都靠它。
/// 每次改值都记一条日志：EC 是现场调出来的参数（速度、超时、标定位置），事后要查得到什么时候把哪一项从多少改成了多少。
/// </summary>
public class EcService : IEcService
{
    public Task<RpcResponse> GetDefinitionsAsync(RpcRequest request, CallContext context = default)
    {
        var items = EcComponent.Current?.Snapshot() ?? [];
        var dtos = items.Select(item => item.Value.ToDto(item.Path)).ToList();
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dtos)));
    }

    public Task<RpcResponse> SetValueAsync(EcSetRequest request, CallContext context = default)
    {
        var ec = EcComponent.Current;
        if (ec is null)
        {
            return Fail(ErrorCodes.EcNotInstalled);
        }

        // 键是"组件全路径.参数名"：最后一个点前面是组件，后面是参数。
        string key = (request.Key ?? string.Empty).Trim();
        int dot = key.LastIndexOf('.');
        if (dot <= 0 || dot == key.Length - 1)
        {
            return Fail(ErrorCodes.EcNotFound, key);
        }

        string path = key[..dot];
        string name = key[(dot + 1)..];
        string old = ec.Get(path, name);
        var result = ec.TrySet(path, name, request.Value ?? string.Empty, out var item);
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
                LogHelper.Info("EC", $"界面改 EC {key}：{old} → {item.Value} {item.Unit}".TrimEnd());
                break;
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(item.ToDto(path))));
    }

    private static Task<RpcResponse> Fail(string code, params string[] args)
    {
        return Task.FromResult(RpcResponse.Fail(code, args));
    }
}
