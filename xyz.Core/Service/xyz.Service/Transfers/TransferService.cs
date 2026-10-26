using System.Globalization;
using ProtoBuf.Grpc;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Transfers;

/// <summary>
/// 搬运 gRPC 服务：启动手动传片并等搬运收尾、出错后人工放锁。校验、锁、执行都在搬运管理里，这里只管翻成回包。
/// </summary>
public class TransferService : BaseService, ITransferService
{
    private readonly ITransferManager? _transfers;

    public TransferService()
    {
        _transfers = IocHelper.GetService<ITransferManager>();
    }

    public async Task<RpcResponse> TransferAsync(RpcRequest request, CallContext context = default)
    {
        if (_transfers is null)
        {
            return RpcResponse.Fail(ErrorCodes.TransferNotInstalled, []);
        }

        if (!request.TryGet<string>(RpcParams.Source, out var source)
            || !request.TryGet<int>(RpcParams.SourceSlot, out var sourceSlot)
            || !request.TryGet<string>(RpcParams.Target, out var target)
            || !request.TryGet<int>(RpcParams.TargetSlot, out var targetSlot))
        {
            return await InvalidRequest();
        }

        string robot = request.Get(RpcParams.Robot, string.Empty).Trim();
        int arm = request.Get(RpcParams.Arm, 0);
        var started = _transfers.Start(new TransferRequest
        {
            Origin = TransferOrigin.Manual,
            Source = source,
            SourceSlot = sourceSlot,
            Target = target,
            TargetSlot = targetSlot,
            Robot = robot.Length == 0 ? null : robot,
            Arm = arm,
        });

        var routine = started.Result;
        if (!started.IsSuccess || routine is null)
        {
            return RpcResponse.Fail(started.ErrorMessage, started.Args);
        }

        int timeout = _transfers.ManualWaitTimeoutMs;
        if (!await Task.Run(() => routine.WaitReply(timeout, context.CancellationToken)).ConfigureAwait(false))
        {
            return RpcResponse.Fail(ErrorCodes.TransferWaitTimeout,
                [routine.Name, timeout.ToString(CultureInfo.InvariantCulture)]);
        }

        if (!routine.IsSuccess)
        {
            return Failed(routine);
        }

        return RpcResponse.Ok(JsonHelper.Serialize(new TransferDoneDto
        {
            Robot = routine.Robot.Name,
            Arm = routine.Arm,
            Source = routine.SourceName,
            SourceSlot = routine.SourceSlot,
            Target = routine.TargetName,
            TargetSlot = routine.TargetSlot,
        }));
    }

    public Task<RpcResponse> ReleaseAsync(RpcRequest request, CallContext context = default)
    {
        if (_transfers is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.TransferNotInstalled, []));
        }

        if (!request.TryGet<Guid>(RpcParams.WaferId, out var waferId))
        {
            return InvalidRequest();
        }

        return Task.FromResult(_transfers.ReleaseHold(waferId)
            ? RpcResponse.Ok()
            : RpcResponse.Fail(ErrorCodes.TransferNotHeld, [waferId.ToString()]));
    }
}
