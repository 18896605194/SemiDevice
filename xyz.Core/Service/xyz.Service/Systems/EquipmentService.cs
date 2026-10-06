using ProtoBuf.Grpc;
using xyz.Common.Log;
using xyz.Components.Enums;
using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Systems;

/// <summary>
/// 整机操作 gRPC 服务（主界面的系统操作）：Auto / Manual 就是开、关搬运管理的自动派单；Stop 关自动派单并中止所有在做的动作；
/// 全部回片把机内的片按来源槽送回去（搬运管理一张一张做）。
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
    /// 整机停止：先关自动派单、撤掉所有搬运单（搬运管理撤：手臂正在取放的由它发设备中止），所有没结束的 Job 走中止流程
    /// （等设备确认、核对片位，不是直接删 Job）；再给正在做别的动作（手动动作）的模块发中止——
    /// 在跑搬运单的机械手、在给 Job 做工艺的腔体归上面两路去停，这里不直接发。闲着的模块不碰。Data 为这里直接发了中止的模块个数。
    /// </summary>
    public Task<RpcResponse> StopAsync(RpcRequest request, CallContext context = default)
    {
        var transfers = TransferManager.Current;
        transfers?.Abort();

        // 不等受理：Job 管理下一拍执行，中止的进展看 Job 推送
        var jobs = JobManager.Current;
        if (jobs is not null)
        {
            _ = jobs.AbortAllAsync(JobCommandSource.Local);
        }

        int aborted = 0;
        foreach (var module in Roots.OfType<BaseModule>())
        {
            if (!module.IsEnabled || module.CurrentOperation is null)
            {
                continue;
            }

            bool robotInTransfer = module is IRobot robot && transfers is not null && transfers.IsRobotInTransfer(robot.Name);
            if (robotInTransfer || JobManager.IsJobProcess(module))
            {
                continue;
            }

            module.Abort();
            aborted++;
        }

        LogHelper.Info(LogModule, $"整机停止：自动派单已关，搬运单已撤，Job 走中止；另有 {aborted} 个模块在做手动动作，已发中止");
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(aborted)));
    }

    public Task<RpcResponse> GetReturnPlanAsync(RpcRequest request, CallContext context = default)
    {
        var transfers = TransferManager.Current;
        if (transfers is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.TransferNotInstalled, []));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(ToDto(transfers.PlanReturnAll()))));
    }

    public Task<RpcResponse> ReturnAllAsync(RpcRequest request, CallContext context = default)
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

        var plan = transfers.StartReturnAll();
        if (plan is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.TransferReturnRunning, []));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(ToDto(plan))));
    }

    private static ReturnPlanDto ToDto(ReturnPlan plan)
    {
        return new ReturnPlanDto
        {
            Moves = plan.Moves.Select(move => new ReturnMoveDto
            {
                WaferId = move.WaferName,
                Source = move.Source,
                SourceSlot = move.SourceSlot,
                SourceIsArm = move.SourceIsArm,
                Target = move.Target,
                TargetSlot = move.TargetSlot,
            }).ToList(),
            Skipped = plan.Skipped.Select(skip => new ReturnSkipDto
            {
                WaferId = skip.WaferName,
                Source = skip.Source,
                SourceSlot = skip.SourceSlot,
                SourceIsArm = skip.SourceIsArm,
                Code = skip.Code,
                Args = skip.Args.ToList(),
            }).ToList(),
        };
    }
}
