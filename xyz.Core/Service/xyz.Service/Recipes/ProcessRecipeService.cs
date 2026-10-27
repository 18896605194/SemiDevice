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
/// 工艺配方
/// </summary>
public class ProcessRecipeService : BaseService, IProcessRecipeService
{
    private readonly IRecipeComponent? _library;

    private readonly IE30RecipeComponent? _e30Recipe;

    private readonly IWaferSize? _waferSize;

    private readonly IMapper _mapper;

    public ProcessRecipeService()
    {
        _library = IocHelper.GetService<IRecipeComponent>();
        _e30Recipe = IocHelper.GetService<IE30RecipeComponent>();
        _waferSize = IocHelper.GetService<IWaferSize>();
        _mapper = IocHelper.GetService<IMapper>() ?? throw new InvalidOperationException("machine.xml 没配映射组件（Mapper）");
    }

    public Task<RpcResponse> GetListAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled(ErrorCodes.ProcessRecipeNotInstalled);
        }

        var dto = new ProcessRecipeListDto
        {
            Capacity = _library.Capacity,
            Items = _mapper.Map<List<ProcessRecipeSummaryDto>>(_library.List()),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> GetAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled(ErrorCodes.ProcessRecipeNotInstalled);
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index))
        {
            return InvalidRequest();
        }

        return ReplyRecipe(_library.Get(index));
    }

    public Task<RpcResponse> GetOptionsAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled(ErrorCodes.ProcessRecipeNotInstalled);
        }

        var stationTypes = _library.StationTypes;
        var dto = new ProcessRecipeOptionsDto
        {
            HeadFields = _library.HeadFields.Select(field => ToHeadDto(field, stationTypes)).ToList(),
            Fields = _library.Fields.Select(ToFieldDto).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled(ErrorCodes.ProcessRecipeNotInstalled);
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        return LockedByHost() ?? ReplyRecipe(_library.Create(index, name, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> RenameAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled(ErrorCodes.ProcessRecipeNotInstalled);
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        return LockedByHost() ?? ReplyRecipe(_library.Rename(index, name, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> DeleteAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled(ErrorCodes.ProcessRecipeNotInstalled);
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index))
        {
            return InvalidRequest();
        }

        return LockedByHost() ?? ReplyRecipe(_library.Delete(index, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> SaveAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled(ErrorCodes.ProcessRecipeNotInstalled);
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<int>(RpcParams.Revision, out var revision)
            || !request.TryGet<List<ProcessRecipeStepDto>>(RpcParams.Steps, out var steps)
            || !request.TryGet<string>(RpcParams.Type, out var type)
            || !request.TryGet<string>(RpcParams.Timeout, out var timeout))
        {
            return InvalidRequest();
        }

        string description = request.Get(RpcParams.Description, string.Empty);
        var head = request.Get(RpcParams.Head, new Dictionary<string, string>());
        var content = new RecipeData
        {
            Type = type,
            Timeout = timeout,
            Description = description,
            Head = ToValues(head),
            Steps = steps.Select(step => ToValues(step.Values)).ToList(),
        };
        return LockedByHost() ?? ReplyRecipe(_library.Save(index, revision, content, RpcOperator.Of(context)));
    }

    private static RecipeStep ToValues(Dictionary<string, string>? values)
    {
        return new RecipeStep
        {
            Values = (values ?? []).Select(pair => new RecipeValue(pair.Key, pair.Value ?? string.Empty)).ToList(),
        };
    }

    /// <summary>
    /// 库的结果翻成回包：成功且带回了工艺配方的转成 DTO 放进 Data，其余照通用的 Reply
    /// </summary>
    private Task<RpcResponse> ReplyRecipe(HandleResult result)
    {
        var recipe = result.Result as RecipeData;
        if (!result.IsSuccess || recipe is null)
        {
            return Reply(result);
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(_mapper.Map<ProcessRecipeDto>(recipe))));
    }

    /// <summary>
    /// 配方头的一项给界面：程序算的标出来；Type 的选项是腔体配的几种 Type，自己加的下拉是 machine.xml 里写的那一套。
    /// Type 必不必填看腔体：有腔体没配 Type（空着也是一种），Type 就能空着，跟后端保存时一样。
    /// </summary>
    private ProcessRecipeFieldDto ToHeadDto(RecipeField field, IReadOnlyList<string> stationTypes)
    {
        bool isType = string.Equals(field.Key, RecipeHead.TypeKey, StringComparison.OrdinalIgnoreCase);
        var dto = ToFieldDto(field);
        dto.IsAuto = RecipeHead.IsAuto(field.Key);
        dto.Options = (isType ? stationTypes : field.OptionsFor(string.Empty)).ToList();

        // 配方头的下拉只看 Options；按 Type 分开的那套不给，界面才不会拿它顶掉 Options
        dto.OptionsByType.Clear();
        if (isType)
        {
            dto.Required = !stationTypes.Contains(string.Empty);
        }

        return dto;
    }

    /// <summary>
    /// 字段给界面：映射组件照抄字段表，上限按当前晶圆尺寸取
    /// </summary>
    private ProcessRecipeFieldDto ToFieldDto(RecipeField field)
    {
        var dto = _mapper.Map<ProcessRecipeFieldDto>(field);
        dto.Max = field.MaxFor(WaferSizeComponent.SizeOf(_waferSize));
        return dto;
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
