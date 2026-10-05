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
/// 搬运 gRPC 服务：手动传片下单并等结果、出错后人工放锁。校验、锁、执行都在搬运管理里，这里只管翻成回包。
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
        var ticket = transfers.Submit(new TransferRequest
        {
            Origin = TransferOrigin.Manual,
            Source = request.Source ?? string.Empty,
            SourceSlot = request.SourceSlot,
            Target = request.Target ?? string.Empty,
            TargetSlot = request.TargetSlot,
            Robot = robot.Length == 0 ? null : robot,
            Arm = request.Arm,
        });

        var completion = ticket.Completion;
        if (!ticket.Accepted || completion is null)
        {
            return RpcResponse.Fail(ticket.Code, ticket.Args);
        }

        int timeout = transfers.ManualWaitTimeoutMs;
        var finished = await Task.WhenAny(completion, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != completion)
        {
            return RpcResponse.Fail(ErrorCodes.TransferWaitTimeout,
                [ticket.Id.ToString(CultureInfo.InvariantCulture), timeout.ToString(CultureInfo.InvariantCulture)]);
        }

        var result = await completion.ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return RpcResponse.Fail(result.Code, result.Args);
        }

        return RpcResponse.Ok(JsonHelper.Serialize(new TransferDoneDto
        {
            Id = result.Id,
            Robot = result.Robot,
            Arm = result.Arm,
            Source = result.Source,
            SourceSlot = result.SourceSlot,
            Target = result.Target,
            TargetSlot = result.TargetSlot,
        }));
    }

    public Task<RpcResponse> ReleaseAsync(TransferReleaseRequest request, CallContext context = default)
    {
        var transfers = TransferManager.Current;
        if (transfers is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.TransferNotInstalled, []));
        }

        return Task.FromResult(transfers.ReleaseHold(request.Id)
            ? RpcResponse.Ok()
            : RpcResponse.Fail(ErrorCodes.TransferNotHeld, [request.Id.ToString(CultureInfo.InvariantCulture)]));
    }
}
