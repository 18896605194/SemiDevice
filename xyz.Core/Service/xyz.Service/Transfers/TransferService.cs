using System.Globalization;
using ProtoBuf.Grpc;
using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Transfers;

/// <summary>
/// 搬运 gRPC 服务：启动手动传片并等操作收尾、出错后人工放锁。校验、锁、执行都在搬运管理里，这里只管翻成回包。
/// </summary>
public class TransferService : BaseService, ITransferService
{
    public TransferService(IReadOnlyList<ComponentBase> roots) : base(roots)
    {
    }

    public async Task<RpcResponse> TransferAsync(TransferRequestDto request, CallContext context = default)
    {
        var transfers = TransferManager.Current;
        if (transfers is null)
        {
            return RpcResponse.Fail(ErrorCodes.TransferNotInstalled, []);
        }

        // protobuf 传输省略默认值字段，空字符串在接收端可能为 null。
        string robot = (request.Robot ?? string.Empty).Trim();
        var started = transfers.Start(new TransferRequest
        {
            Origin = TransferOrigin.Manual,
            Source = request.Source ?? string.Empty,
            SourceSlot = request.SourceSlot,
            Target = request.Target ?? string.Empty,
            TargetSlot = request.TargetSlot,
            Robot = robot.Length == 0 ? null : robot,
            Arm = request.Arm,
        });

        var operation = started.Result;
        if (!started.IsSuccess || operation is null)
        {
            return RpcResponse.Fail(started.ErrorMessage, started.Args);
        }

        int timeout = transfers.ManualWaitTimeoutMs;
        if (!await Task.Run(() => operation.WaitReply(timeout, context.CancellationToken)).ConfigureAwait(false))
        {
            return RpcResponse.Fail(ErrorCodes.TransferWaitTimeout,
                [operation.Name, timeout.ToString(CultureInfo.InvariantCulture)]);
        }

        if (!operation.IsSuccess)
        {
            return RpcResponse.Fail(operation.Code, operation.ErrorArgs);
        }

        return RpcResponse.Ok(JsonHelper.Serialize(new TransferDoneDto
        {
            Robot = operation.Robot.Name,
            Arm = operation.Arm,
            Source = operation.SourceName,
            SourceSlot = operation.SourceSlot,
            Target = operation.Target.Name,
            TargetSlot = operation.TargetSlot,
        }));
    }

    public Task<RpcResponse> ReleaseAsync(TransferReleaseRequest request, CallContext context = default)
    {
        var transfers = TransferManager.Current;
        if (transfers is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.TransferNotInstalled, []));
        }

        return Task.FromResult(transfers.ReleaseHold(request.WaferId)
            ? RpcResponse.Ok()
            : RpcResponse.Fail(ErrorCodes.TransferNotHeld, [request.WaferId.ToString()]));
    }
}
