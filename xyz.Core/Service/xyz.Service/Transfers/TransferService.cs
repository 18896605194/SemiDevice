using System.Globalization;
using ProtoBuf.Grpc;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Transfers;

/// <summary>
/// 搬运服务
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
            return await NotInstalled(ErrorCodes.TransferNotInstalled);
        }
        //校验
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
            Origin = TransferOrigin.Manual, //操作模式手动还是自动
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
        bool replied = await Task.Run(() => routine.WaitReply(timeout, context.CancellationToken)).ConfigureAwait(false);
        if (!replied)
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
            return NotInstalled(ErrorCodes.TransferNotInstalled);
        }

        if (!request.TryGet<Guid>(RpcParams.WaferId, out var waferId))
        {
            return InvalidRequest();
        }

        if (!_transfers.ReleaseHold(waferId))
        {
            return Fail(ErrorCodes.TransferNotHeld, waferId.ToString());
        }

        return Task.FromResult(RpcResponse.Ok());
    }
}
