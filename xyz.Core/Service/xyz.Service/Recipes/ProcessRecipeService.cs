using ProtoBuf.Grpc;
using xyz.Components;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Recipes;

/// <summary>
/// 工艺配方 gRPC 服务（配方 → 工艺配方页；流程配方页、腔体手动页选工艺配方也用它的列表）：把工艺配方库（ProcessRecipeComponent）的结果翻成回包。
/// 检查都在库里做（编号、名称、版本、步骤），这里只管取库、转 DTO、带上操作人。字段表（每一步有哪些字段）也从库里取，界面按它生成步骤表。
/// </summary>
public class ProcessRecipeService : BaseService, IProcessRecipeService
{
    /// <summary>
    /// 客户端没带操作人时记成这个（客户端还没做登录前都会带上当前用户名，这里只是兜底）。
    /// </summary>
    private const string UnknownOperator = "Unknown";

    public ProcessRecipeService(IReadOnlyList<ComponentBase> roots) : base(roots)
    {
    }

    public Task<RpcResponse> GetListAsync(RpcRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        if (library is null)
        {
            return NotInstalled();
        }

        var dto = new ProcessRecipeListDto
        {
            Capacity = library.Capacity,
            Items = library.List().Select(item => new ProcessRecipeSummaryDto
            {
                Index = item.Index,
                Name = item.Name,
                Description = item.Description,
                TotalSeconds = item.TotalSeconds,
            }).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> GetAsync(ProcessRecipeIndexRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        return library is null ? NotInstalled() : Reply(library.Get(request.Index));
    }

    public Task<RpcResponse> GetOptionsAsync(RpcRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        if (library is null)
        {
            return NotInstalled();
        }

        var dto = new ProcessRecipeOptionsDto
        {
            Fields = library.Fields.Select(field => ToDto(field, library.ChoicesOf(field))).ToList(),
            MaxTotalSeconds = library.MaxTotalSeconds,
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> CreateAsync(ProcessRecipeCreateRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        return library is null ? NotInstalled() : Reply(library.Create(request.Index, request.Name ?? string.Empty, OperatorOf(request.Operator)));
    }

    public Task<RpcResponse> RenameAsync(ProcessRecipeRenameRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        return library is null ? NotInstalled() : Reply(library.Rename(request.Index, request.Name ?? string.Empty, OperatorOf(request.Operator)));
    }

    public Task<RpcResponse> DeleteAsync(ProcessRecipeDeleteRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        return library is null ? NotInstalled() : Reply(library.Delete(request.Index, OperatorOf(request.Operator)));
    }

    public Task<RpcResponse> SaveAsync(ProcessRecipeSaveRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        if (library is null)
        {
            return NotInstalled();
        }

        // protobuf 收到的空列表、空字符串可能是 null，进库之前都换成空的
        var steps = (request.Steps ?? []).Select(step => new ProcessRecipeStep
        {
            Values = (step.Values ?? []).Select(pair => new ProcessRecipeValue(pair.Key, pair.Value ?? string.Empty)).ToList(),
        }).ToList();
        return Reply(library.Save(request.Index, request.Revision, request.Description ?? string.Empty, steps, OperatorOf(request.Operator)));
    }

    private static Task<RpcResponse> Reply(ProcessRecipeResult result)
    {
        if (!result.IsOk)
        {
            return Task.FromResult(RpcResponse.Fail(result.Code, result.Args));
        }

        var recipe = result.Recipe;
        return Task.FromResult(recipe is null ? RpcResponse.Ok() : RpcResponse.Ok(JsonHelper.Serialize(ToDto(recipe))));
    }

    private static ProcessRecipeDto ToDto(ProcessRecipeData data)
    {
        return new ProcessRecipeDto
        {
            Index = data.Index,
            Name = data.Name,
            Description = data.Description,
            CreatedBy = data.CreatedBy,
            CreatedAt = data.CreatedAt,
            ModifiedBy = data.ModifiedBy,
            ModifiedAt = data.ModifiedAt,
            Revision = data.Revision,
            Steps = data.Steps.Select(ToDto).ToList(),
        };
    }

    private static ProcessRecipeStepDto ToDto(ProcessRecipeStep step)
    {
        var dto = new ProcessRecipeStepDto();
        foreach (var value in step.Values)
        {
            dto.Values.TryAdd(value.Name, value.Value);
        }

        return dto;
    }

    /// <summary>
    /// 一个字段给界面：下拉带上取好的选项（跟着别的字段走的按那个字段的值分开给）。
    /// </summary>
    private static ProcessRecipeFieldDto ToDto(ProcessRecipeField field, ProcessRecipeChoices choices)
    {
        return new ProcessRecipeFieldDto
        {
            Key = field.Key,
            Text = field.Text,
            TextEn = field.TextEn,
            Type = field.Type,
            Unit = field.Unit,
            Min = field.Min,
            Max = field.Max,
            Decimals = field.Decimals,
            Default = field.Default,
            Required = field.Required,
            ParentKey = field.Source?.ParentKey ?? string.Empty,
            Options = choices.Values.ToList(),
            OptionsByParent = choices.ByParent.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
        };
    }

    private static string OperatorOf(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? UnknownOperator : name.Trim();
    }

    private static Task<RpcResponse> NotInstalled()
    {
        return Task.FromResult(RpcResponse.Fail(ErrorCodes.ProcessRecipeNotInstalled, []));
    }
}
