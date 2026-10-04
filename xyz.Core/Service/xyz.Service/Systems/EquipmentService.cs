using ProtoBuf.Grpc;
using xyz.Common.Log;
using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Systems;

/// <summary>
/// 整机操作 gRPC 服务（主界面的系统操作）：Auto / Manual 就是开、关搬运管理的自动派单；Stop 关自动派单并中止所有在做的动作。
/// 模式、系统状态由设备总状态推送带给界面（EquipmentStatusPublisher），这里只管改。
/// </summary>
public class EquipmentService : BaseService, IEquipmentService
{
    private const string LogModule = "Equipment";

    public EquipmentService(IReadOnlyList<ComponentBase> roots) : base(roots)
    {
    }

    public Task<RpcResponse> AutoAsync(RpcRequest request, CallContext context = default)
    {
        var transfers = TransferManager.Current;
        if (transfers is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.TransferNotInstalled, []));
        }

        if (!transfers.IsEnable)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.TransferDisabled, []));
        }

        transfers.StartAutoDispatch();
        return Task.FromResult(RpcResponse.Ok());
    }

    public Task<RpcResponse> ManualAsync(RpcRequest request, CallContext context = default)
    {
        TransferManager.Current?.StopAutoDispatch();
        return Task.FromResult(RpcResponse.Ok());
    }

    /// <summary>
    /// 先关自动派单（停下来之后不能再派出新的一趟），再给正在执行动作的模块发中止；闲着的模块不碰，免得白发设备指令。
    /// 中止是急停，可以顶替在途动作，所以不看模块现在是什么状态。
    /// </summary>
    public Task<RpcResponse> StopAsync(RpcRequest request, CallContext context = default)
    {
        TransferManager.Current?.Abort();

        int aborted = 0;
        foreach (var module in Roots.OfType<BaseModule>())
        {
            if (!module.IsEnabled || module.CurrentOperation is null)
            {
                continue;
            }

            module.Abort();
            aborted++;
        }

        LogHelper.Info(LogModule, $"整机停止：自动派单已关，{aborted} 个模块在执行动作，已发中止");
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(aborted)));
    }
}
