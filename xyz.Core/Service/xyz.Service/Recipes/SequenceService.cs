using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
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
    private readonly IMapper _mapper;

    public SequenceService()
    {
        _sequence = IocHelper.GetService<ISequenceComponent>();
        _e30Recipe = IocHelper.GetService<IE30RecipeComponent>();
        _mapper = IocHelper.GetService<IMapper>() ?? throw new InvalidOperationException("machine.xml 没配映射组件（Mapper）");
    }

    public Task<RpcResponse> GetListAsync(RpcRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled(ErrorCodes.SequenceNotInstalled);
        }

        var dto = new SequenceListDto
        {
            Capacity = _sequence.Capacity,
            Items = _mapper.Map<List<SequenceSummaryDto>>(_sequence.List()),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> GetAsync(RpcRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled(ErrorCodes.SequenceNotInstalled);
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index))
        {
            return InvalidRequest();
        }

        return ReplySequence(_sequence.Get(index));
    }

    public Task<RpcResponse> GetStationGroupsAsync(RpcRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled(ErrorCodes.SequenceNotInstalled);
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

    public Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled(ErrorCodes.SequenceNotInstalled);
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        return LockedByHost() ?? ReplySequence(_sequence.Create(index, name, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> RenameAsync(RpcRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled(ErrorCodes.SequenceNotInstalled);
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        return LockedByHost() ?? ReplySequence(_sequence.Rename(index, name, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> DeleteAsync(RpcRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled(ErrorCodes.SequenceNotInstalled);
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index))
        {
            return InvalidRequest();
        }

        return LockedByHost() ?? ReplySequence(_sequence.Delete(index, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> SaveAsync(RpcRequest request, CallContext context = default)
    {
        if (_sequence is null)
        {
            return NotInstalled(ErrorCodes.SequenceNotInstalled);
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<int>(RpcParams.Revision, out var revision)
            || !request.TryGet<List<SequenceStepDto>>(RpcParams.Steps, out var steps))
        {
            return InvalidRequest();
        }

        string description = request.Get(RpcParams.Description, string.Empty);
        // 步骤里写成 null 的字段（JSON 里给了 null），进库之前都换成空的
        var sequenceSteps = steps.Select(step => new SequenceStep(
            step.Group ?? string.Empty,
            (step.Stations ?? []).Select(station => station ?? string.Empty),
            step.Recipe ?? string.Empty)).ToList();
        return LockedByHost() ?? ReplySequence(_sequence.Save(index, revision, description, sequenceSteps, RpcOperator.Of(context)));
    }

    /// <summary>
    /// 库的结果翻成回包：成功且带回了流程配方的转成 DTO 放进 Data，其余照通用的 Reply
    /// </summary>
    private Task<RpcResponse> ReplySequence(HandleResult result)
    {
        var sequence = result.Result as SequenceData;
        if (!result.IsSuccess || sequence is null)
        {
            return Reply(result);
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(_mapper.Map<SequenceDto>(sequence))));
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

        return Fail(ErrorCodes.RecipeLockedByHost);
    }
}
