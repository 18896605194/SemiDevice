using System.Globalization;
using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components;
using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Systems;

/// <summary>
/// 整机操作 gRPC 服务（主界面的系统操作）：全部初始化、Auto / Manual、整机停止、全部回片都转给整机模块（<see cref="SystemModule"/>），
/// 这里只翻结果：开了就回，进展看系统状态推送（SystemStateDto）；被拒照原样回整机给的错误码和参数。另有切晶圆尺寸。
/// </summary>
public class EquipmentService : BaseService, IEquipmentService
{
    private readonly ISystemModule? _system;

    private readonly IWaferSize? _waferSize;

    private readonly ILedgerManager? _wafers;

    public EquipmentService()
    {
        _system = IocHelper.GetService<ISystemModule>();
        _waferSize = IocHelper.GetService<IWaferSize>();
        _wafers = IocHelper.GetService<ILedgerManager>();
    }

    public Task<RpcResponse> InitAsync(RpcRequest request, CallContext context = default)
    {
        if (_system is null)
        {
            return NotInstalled(ErrorCodes.SystemNotInstalled);
        }

        return Reply(_system.InitAll());
    }

    public Task<RpcResponse> AutoAsync(RpcRequest request, CallContext context = default)
    {
        if (_system is null)
        {
            return NotInstalled(ErrorCodes.SystemNotInstalled);
        }

        return Reply(_system.Auto());
    }

    public Task<RpcResponse> ManualAsync(RpcRequest request, CallContext context = default)
    {
        if (_system is null)
        {
            return NotInstalled(ErrorCodes.SystemNotInstalled);
        }

        return Reply(_system.Manual());
    }

    public Task<RpcResponse> StopAsync(RpcRequest request, CallContext context = default)
    {
        if (_system is null)
        {
            return NotInstalled(ErrorCodes.SystemNotInstalled);
        }

        if (_system.Abort() is not null)
        {
            return Task.FromResult(RpcResponse.Ok());
        }

        // 被拒了再问原因（停止中再按）
        var rejected = _system.CheckAbort();
        if (rejected is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.ActionRejected,
                [_system.Name, _system.State.ToString(CultureInfo.InvariantCulture)]));
        }

        return Reply(rejected);
    }

    public Task<RpcResponse> ReturnAllAsync(RpcRequest request, CallContext context = default)
    {
        if (_system is null)
        {
            return NotInstalled(ErrorCodes.SystemNotInstalled);
        }

        return Reply(_system.ReturnAll());
    }

    /// <summary>
    /// 切晶圆尺寸：整机没初始化（启用的模块都是未初始化）、机台里（LoadPort 以外的位置）没片才让切——
    /// 摆臂马上按新尺寸的半径和边缘示教位换算，机台里留着的片、初始化好的设备都还是按旧尺寸来的。LoadPort 上的片盒不算，换尺寸本来要换盒。
    /// </summary>
    public Task<RpcResponse> SetWaferSizeAsync(RpcRequest request, CallContext context = default)
    {
        if (!request.TryGet<int>(RpcParams.Size, out var size))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaferSizeInvalid, [string.Empty]));
        }

        if (!WaferSizeComponent.IsSupported(size))
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaferSizeInvalid, [size.ToString(CultureInfo.InvariantCulture)]));
        }

        if (_waferSize is null)
        {
            return NotInstalled(ErrorCodes.WaferSizeNotInstalled);
        }

        if (_waferSize.Size == size)
        {
            return Task.FromResult(RpcResponse.Ok());
        }

        // 整机不算模块，单独看：整机初始化过也不让切
        var system = _system;
        if (system is not null && system.State != ModuleState.NotInit)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaferSizeInitialized, [system.Name]));
        }

        foreach (var module in IocHelper.GetServices<IModule>())
        {
            if (module.State != ModuleState.NotInit)
            {
                return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaferSizeInitialized, [module.Name]));
            }
        }

        if (_wafers is not null)
        {
            foreach (var (location, _) in _wafers.Locations)
            {
                if (!_wafers.IsLoadPort(location) && _wafers.CountWafers(location) > 0)
                {
                    return Task.FromResult(RpcResponse.Fail(ErrorCodes.WaferSizeHasWafer, [location]));
                }
            }
        }

        return Reply(_waferSize.SetSize(size));
    }
}
