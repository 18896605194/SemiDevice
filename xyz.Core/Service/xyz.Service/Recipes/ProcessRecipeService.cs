using ProtoBuf.Grpc;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Recipes;

/// <summary>
/// 工艺配方 gRPC 服务（配方 → 工艺配方页；流程配方页、腔体手动页选工艺配方也用它的列表）：把工艺配方库（RecipeComponent）的结果翻成回包。
/// 检查都在库里做（编号、名称、版本、步骤），这里只管取库、转 DTO、带上操作人。字段表（每一步有哪些字段）也从库里取，界面按它生成步骤表。
/// </summary>
public class ProcessRecipeService : BaseService, IProcessRecipeService
{
    private readonly IRecipeComponent? _library;

    /// <summary>
    /// EAP 的远程配方管理：Host 锁着时本地不让改
    /// </summary>
    private readonly IE30RecipeComponent? _e30Recipe;

    /// <summary>
    /// 晶圆尺寸：字段的上限按它取
    /// </summary>
    private readonly IWaferSize? _waferSize;

    public ProcessRecipeService()
    {
        _library = IocHelper.GetService<IRecipeComponent>();
        _e30Recipe = IocHelper.GetService<IE30RecipeComponent>();
        _waferSize = IocHelper.GetService<IWaferSize>();
    }

    public Task<RpcResponse> GetListAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        var dto = new ProcessRecipeListDto
        {
            Capacity = _library.Capacity,
            Items = _library.List().Select(item => new ProcessRecipeSummaryDto
            {
                Index = item.Index,
                Name = item.Name,
                Description = item.Description,
                TotalSeconds = item.TotalSeconds,
            }).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> GetAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index))
        {
            return InvalidRequest();
        }

        return Reply(_library.Get(index));
    }

    public Task<RpcResponse> GetOptionsAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        var stationTypes = _library.StationTypes;
        var dto = new ProcessRecipeOptionsDto
        {
            HeadFields = _library.HeadFields.Select(field => ToHeadDto(field, stationTypes)).ToList(),
            Fields = _library.Fields.Select(ToDto).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> CreateAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        return LockedByHost() ?? Reply(_library.Create(index, name, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> RenameAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index)
            || !request.TryGet<string>(RpcParams.Name, out var name))
        {
            return InvalidRequest();
        }

        return LockedByHost() ?? Reply(_library.Rename(index, name, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> DeleteAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
        }

        if (!request.TryGet<int>(RpcParams.Index, out var index))
        {
            return InvalidRequest();
        }

        return LockedByHost() ?? Reply(_library.Delete(index, RpcOperator.Of(context)));
    }

    public Task<RpcResponse> SaveAsync(RpcRequest request, CallContext context = default)
    {
        if (_library is null)
        {
            return NotInstalled();
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
        return LockedByHost() ?? Reply(_library.Save(index, revision, content, RpcOperator.Of(context)));
    }

    private static RecipeStep ToValues(Dictionary<string, string>? values)
    {
        return new RecipeStep
        {
            Values = (values ?? []).Select(pair => new RecipeValue(pair.Key, pair.Value ?? string.Empty)).ToList(),
        };
    }

    private static Task<RpcResponse> Reply(HandleResult result)
    {
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        var recipe = result.Result as RecipeData;
        return Task.FromResult(recipe is null ? RpcResponse.Ok() : RpcResponse.Ok(JsonHelper.Serialize(ToDto(recipe))));
    }

    private static ProcessRecipeDto ToDto(RecipeData data)
    {
        return new ProcessRecipeDto
        {
            Index = data.Index,
            Name = data.Name,
            Type = data.Type,
            Timeout = data.Timeout,
            Description = data.Description,
            Head = ToDto(data.Head).Values,
            CreatedBy = data.CreatedBy,
            CreatedAt = data.CreatedAt,
            ModifiedBy = data.ModifiedBy,
            ModifiedAt = data.ModifiedAt,
            Revision = data.Revision,
            Steps = data.Steps.Select(ToDto).ToList(),
        };
    }

    private static ProcessRecipeStepDto ToDto(RecipeStep step)
    {
        var dto = new ProcessRecipeStepDto();
        foreach (var value in step.Values)
        {
            dto.Values.TryAdd(value.Name, value.Value);
        }

        return dto;
    }

    /// <summary>
    /// 步骤的一个字段给界面：下拉带上 machine.xml 里按站点 Type 分开写的选项。
    /// </summary>
    private ProcessRecipeFieldDto ToDto(RecipeField field)
    {
        var dto = ToFieldDto(field);
        dto.OptionsByType = field.OptionsByType.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
        return dto;
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
        if (isType)
        {
            dto.Required = !stationTypes.Contains(string.Empty);
        }

        return dto;
    }

    private ProcessRecipeFieldDto ToFieldDto(RecipeField field)
    {
        return new ProcessRecipeFieldDto
        {
            Key = field.Key,
            Text = field.Text,
            TextEn = field.TextEn,
            Type = field.Type,
            Unit = field.Unit,
            Min = field.Min,
            Max = field.MaxFor(WaferSizeComponent.SizeOf(_waferSize)),
            Decimals = field.Decimals,
            Default = field.Default,
            Required = field.Required,
        };
    }

    private static Task<RpcResponse> NotInstalled()
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.ProcessRecipeNotInstalled, []));
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
