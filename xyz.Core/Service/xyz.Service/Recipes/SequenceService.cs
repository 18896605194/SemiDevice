using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Recipes;

/// <summary>
/// 流程配方 gRPC 服务（配方 → 流程配方页）：把流程配方库（SequenceComponent）的结果翻成回包。
/// 检查都在库里做（编号、名称、版本、步骤），这里只管取库、转 DTO、带上操作人。
/// </summary>
public class SequenceService : BaseService, ISequenceService
{
    /// <summary>
    /// 客户端没带操作人时记成这个（客户端还没做登录前都会带上当前用户名，这里只是兜底）。
    /// </summary>
    private const string UnknownOperator = "Unknown";

    private readonly ISequenceComponent? _library;

    /// <summary>
    /// EAP 的远程配方管理：Host 锁着时本地不让改
    /// </summary>
    private readonly IE30RecipeComponent? _e30Recipe;

    public SequenceService()
    {
        _library = IocHelper.GetService<ISequenceComponent>();
        _e30Recipe = IocHelper.GetService<IE30RecipeComponent>();
    }

    public Task<RpcResponse> GetListAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        var dto = new SequenceListDto
        {
            Capacity = _library.Capacity,
            Items = _library.List().Select(item => new SequenceSummaryDto { Index = item.Index, Name = item.Name }).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> GetAsync(SequenceIndexRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        return Reply(_library.Get(request.Index));
    }

    public Task<RpcResponse> GetStationGroupsAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        var groups = _library.StationGroups.Select(group => new SequenceStationGroupDto
        {
            Name = group.Name,
            Modules = group.Modules.ToList(),
            IsLoadPort = group.IsLoadPort,
            NeedsRecipe = group.NeedsRecipe,
        }).ToList();
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(groups)));
    }

    public Task<RpcResponse> CreateAsync(SequenceCreateRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        return LockedByHost() ?? Reply(_library.Create(request.Index, request.Name ?? string.Empty, OperatorOf(request.Operator)));
    }

    public Task<RpcResponse> RenameAsync(SequenceRenameRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        return LockedByHost() ?? Reply(_library.Rename(request.Index, request.Name ?? string.Empty, OperatorOf(request.Operator)));
    }

    public Task<RpcResponse> DeleteAsync(SequenceDeleteRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        return LockedByHost() ?? Reply(_library.Delete(request.Index, OperatorOf(request.Operator)));
    }

    public Task<RpcResponse> SaveAsync(SequenceSaveRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        // protobuf 收到的空列表、空字符串可能是 null，进库之前都换成空的
        var steps = (request.Steps ?? []).Select(step => new SequenceStep(
            step.Group ?? string.Empty,
            (step.Stations ?? []).Select(station => station ?? string.Empty),
            step.Recipe ?? string.Empty)).ToList();
        return LockedByHost() ?? Reply(_library.Save(request.Index, request.Revision, request.Description ?? string.Empty, steps, OperatorOf(request.Operator)));
    }

    private static Task<RpcResponse> Reply(HandleResult result)
    {
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        var sequence = result.Result as SequenceData;
        return Task.FromResult(sequence is null ? RpcResponse.Ok() : RpcResponse.Ok(JsonHelper.Serialize(ToDto(sequence))));
    }

    private static SequenceDto ToDto(SequenceData data)
    {
        return new SequenceDto
        {
            Index = data.Index,
            Name = data.Name,
            Description = data.Description,
            CreatedBy = data.CreatedBy,
            CreatedAt = data.CreatedAt,
            ModifiedBy = data.ModifiedBy,
            ModifiedAt = data.ModifiedAt,
            Revision = data.Revision,
            Steps = data.Steps.Select(step => new SequenceStepDto
            {
                Group = step.Group,
                Stations = step.Stations.ToList(),
                Recipe = step.Recipe,
            }).ToList(),
        };
    }

    private static string OperatorOf(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? UnknownOperator : name.Trim();
    }

    private static Task<RpcResponse> NotInstalled()
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.SequenceNotInstalled, []));
    }

    /// <summary>
    /// ON-LINE REMOTE 时 Host 锁着本地改配方（sc.xml Eap.Recipe 的 LockLocalEditInRemote）：返回拒绝的回包，没锁返回 null
    /// </summary>
    private Task<RpcResponse>? LockedByHost()
    {
        if (_e30Recipe is null || !_e30Recipe.IsLocalEditLocked)
        {
            return null;
        }

        return Task.FromResult(RpcResponse.Fail(ErrorCodes.RecipeLockedByHost, []));
    }
}
