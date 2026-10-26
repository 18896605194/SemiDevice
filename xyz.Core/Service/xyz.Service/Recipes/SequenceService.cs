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
/// 流程配方服务
/// </summary>
public class SequenceService : BaseService, ISequenceService
{
    private readonly ISequenceComponent? _sequence;
    private readonly IE30RecipeComponent? _e30Recipe;

    public SequenceService()
    {
        _sequence = IocHelper.GetService<ISequenceComponent>();
        _e30Recipe = IocHelper.GetService<IE30RecipeComponent>();
    }

    public Task<RpcResponse> GetListAsync(RpcRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled();
        }

        var dto = new SequenceListDto
        {
            Capacity = _sequence.Capacity,
            Items = _sequence.List().Select(item => new SequenceSummaryDto { Index = item.Index, Name = item.Name }).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> GetAsync(SequenceIndexRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled();
        }

        return Reply(_sequence.Get(request.Index));
    }

    public Task<RpcResponse> GetStationGroupsAsync(RpcRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled();
        }

        var groups = _sequence.StationGroups.Select(group => new SequenceStationGroupDto
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
        if (_sequence is null)
        {
            return NotInstalled();
        }

        return LockedByHost() ?? Reply(_sequence.Create(request.Index, request.Name ?? string.Empty, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> RenameAsync(SequenceRenameRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled();
        }

        return LockedByHost() ?? Reply(_sequence.Rename(request.Index, request.Name ?? string.Empty, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> DeleteAsync(SequenceDeleteRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled();
        }

        return LockedByHost() ?? Reply(_sequence.Delete(request.Index, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> SaveAsync(SequenceSaveRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled();
        }

        // protobuf 收到的空列表、空字符串可能是 null，进库之前都换成空的
        var steps = (request.Steps ?? []).Select(step => new SequenceStep(
            step.Group ?? string.Empty,
            (step.Stations ?? []).Select(station => station ?? string.Empty),
            step.Recipe ?? string.Empty)).ToList();
        return LockedByHost() ?? Reply(_sequence.Save(request.Index, request.Revision, request.Description ?? string.Empty, steps, RpcOperator.Of(context)));
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

    private static Task<RpcResponse> NotInstalled()
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.SequenceNotInstalled, []));
    }

    /// <summary>
    /// ON-LINE REMOTE 时 Host 锁着本地改配方（machine.xml Eap.Recipe 的 LockLocalEditInRemote）：返回拒绝的回包，没锁返回 null
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
