using System.Globalization;
using ProtoBuf.Grpc;
using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements;
using xyz.Elements.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Systems;

/// <summary>
/// 整机操作服务
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

        return Reply(_system.Abort());
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
    /// 切晶圆尺寸
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public Task<RpcResponse> SetWaferSizeAsync(RpcRequest request, CallContext context = default)
    {
        if (!request.TryGet<int>(RpcParams.Size, out var size))
        {
            return Fail(ErrorCodes.WaferSizeInvalid, string.Empty);
        }

        if (!WaferSizeElement.IsSupported(size))
        {
            return Fail(ErrorCodes.WaferSizeInvalid, size.ToString(CultureInfo.InvariantCulture));
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
        if (_system is not null && _system.State != ModuleState.NotInit)
        {
            return Fail(ErrorCodes.WaferSizeInitialized, _system.Name);
        }

        foreach (var module in IocHelper.GetServices<IModule>())
        {
            if (module.State != ModuleState.NotInit)
            {
                return Fail(ErrorCodes.WaferSizeInitialized, module.Name);
            }
        }

        if (_wafers is not null)
        {
            foreach (var (location, _) in _wafers.Locations)
            {
                if (!_wafers.IsLoadPort(location) && _wafers.CountWafers(location) > 0)
                {
                    return Fail(ErrorCodes.WaferSizeHasWafer, location);
                }
            }
        }

        return Reply(_waferSize.SetSize(size));
    }
}
