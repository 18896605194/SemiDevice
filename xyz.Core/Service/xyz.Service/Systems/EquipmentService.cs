using System.Globalization;
using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;

namespace xyz.Service.Systems;

/// <summary>
/// 整机操作 gRPC 服务（主界面的系统操作）：全部初始化、Auto / Manual、整机停止、全部回片都转给整机模块（<see cref="SystemModule"/>），
/// 这里只翻结果：开了就回，进展看系统状态推送（SystemStateDto）；被拒照原样回整机给的错误码和参数。另有切晶圆尺寸。
/// </summary>
public class EquipmentService : BaseService, IEquipmentService
{
    public EquipmentService(IReadOnlyList<ComponentBase> roots) : base(roots)
    {
    }

    public Task<RpcResponse> InitAsync(RpcRequest request, CallContext context = default)
    {
        var system = SystemModule.Current;
        if (system is null)
        {
            return SystemNotInstalled();
        }

        return Reply(system.InitAll());
    }

    public Task<RpcResponse> AutoAsync(RpcRequest request, CallContext context = default)
    {
        var system = SystemModule.Current;
        if (system is null)
        {
            return SystemNotInstalled();
        }

        return Reply(system.Auto());
    }

    public Task<RpcResponse> ManualAsync(RpcRequest request, CallContext context = default)
    {
        var system = SystemModule.Current;
        if (system is null)
        {
            return SystemNotInstalled();
        }

        return Reply(system.Manual());
    }

    public Task<RpcResponse> StopAsync(RpcRequest request, CallContext context = default)
    {
        var system = SystemModule.Current;
        if (system is null)
        {
            return SystemNotInstalled();
        }

        if (system.Abort() is not null)
        {
            return Task.FromResult(RpcResponse.Ok());
        }

        // 被拒了再问原因（停止中再按）
        var rejected = system.CheckAbort();
        if (rejected is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ActionRejected,
                [system.Name, system.State.ToString(CultureInfo.InvariantCulture)]));
        }

        return Reply(rejected);
    }

    public Task<RpcResponse> ReturnAllAsync(RpcRequest request, CallContext context = default)
    {
        var system = SystemModule.Current;
        if (system is null)
        {
            return SystemNotInstalled();
        }

        return Reply(system.ReturnAll());
    }

    private static Task<RpcResponse> SystemNotInstalled()
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.SystemNotInstalled, []));
    }

    private static Task<RpcResponse> Reply(HandleResult result)
    {
        return Task.FromResult(result.IsSuccess ? RpcResponse.Ok() : RpcResponse.Fail(result.ErrorMessage, result.Args));
    }

    private static Task<RpcResponse> Reply(HandleResult<ModuleOperation> result)
    {
        return Task.FromResult(result.IsSuccess ? RpcResponse.Ok() : RpcResponse.Fail(result.ErrorMessage, result.Args));
    }

    /// <summary>
    /// 切晶圆尺寸：整机没初始化（启用的模块都是未初始化）、机台里（LoadPort 以外的位置）没片才让切——
    /// 摆臂马上按新尺寸的半径和边缘示教位换算，机台里留着的片、初始化好的设备都还是按旧尺寸来的。LoadPort 上的片盒不算，换尺寸本来要换盒。
    /// </summary>
    public Task<RpcResponse> SetWaferSizeAsync(RpcRequest request, CallContext context = default)
    {
        request.Parameters.TryGetValue(IEquipmentService.WaferSizeKey, out string? sizeText);
        sizeText ??= string.Empty;
        if (!int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out int size) || !WaferSizeComponent.IsSupported(size))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaferSizeInvalid, [sizeText]));
        }

        var waferSize = WaferSizeComponent.Current;
        if (waferSize is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaferSizeNotInstalled, []));
        }

        if (waferSize.Size == size)
        {
            return Task.FromResult(RpcResponse.Ok());
        }

        foreach (var module in Roots.OfType<BaseModule>())
        {
            if (module.IsEnabled && module.State != ModuleState.NotInit)
            {
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaferSizeInitialized, [module.Name]));
            }
        }

        var wafers = WaferManagerComponent.Current;
        if (wafers is not null)
        {
            foreach (var (location, _) in wafers.Locations)
            {
                if (!wafers.IsLoadPort(location) && wafers.CountWafers(location) > 0)
                {
                    return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaferSizeHasWafer, [location]));
                }
            }
        }

        var result = waferSize.SetSize(size);
        return Task.FromResult(result.IsSuccess ? RpcResponse.Ok() : RpcResponse.Fail(result.ErrorMessage, result.Args));
    }
}
