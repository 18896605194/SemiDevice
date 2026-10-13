using ProtoBuf.Grpc;
using xyz.Components;
using xyz.Components.Components;
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

        var stationTypes = library.StationTypes;
        var dto = new ProcessRecipeOptionsDto
        {
            HeadFields = library.HeadFields.Select(field => ToHeadDto(field, stationTypes)).ToList(),
            Fields = library.Fields.Select(ToDto).ToList(),
        };
        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(dto)));
    }

    public Task<RpcResponse> CreateAsync(ProcessRecipeCreateRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        return library is null ? NotInstalled() : LockedByHost() ?? Reply(library.Create(request.Index, request.Name ?? string.Empty, OperatorOf(request.Operator)));
    }

    public Task<RpcResponse> RenameAsync(ProcessRecipeRenameRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        return library is null ? NotInstalled() : LockedByHost() ?? Reply(library.Rename(request.Index, request.Name ?? string.Empty, OperatorOf(request.Operator)));
    }

    public Task<RpcResponse> DeleteAsync(ProcessRecipeDeleteRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        return library is null ? NotInstalled() : LockedByHost() ?? Reply(library.Delete(request.Index, OperatorOf(request.Operator)));
    }

    public Task<RpcResponse> SaveAsync(ProcessRecipeSaveRequest request, CallContext context = default)
    {
        var library = ProcessRecipeComponent.Current;
        if (library is null)
        {
            return NotInstalled();
        }

        // protobuf 收到的空列表、空字符串可能是 null，进库之前都换成空的
        var content = new ProcessRecipeData
        {
            Type = request.Type ?? string.Empty,
            Timeout = request.Timeout ?? string.Empty,
            Description = request.Description ?? string.Empty,
            Head = ToValues(request.Head),
            Steps = (request.Steps ?? []).Select(step => ToValues(step.Values)).ToList(),
        };
        return LockedByHost() ?? Reply(library.Save(request.Index, request.Revision, content, OperatorOf(request.Operator)));
    }

    private static ProcessRecipeStep ToValues(Dictionary<string, string>? values)
    {
        return new ProcessRecipeStep
        {
            Values = (values ?? []).Select(pair => new ProcessRecipeValue(pair.Key, pair.Value ?? string.Empty)).ToList(),
        };
    }

    private static Task<RpcResponse> Reply(HandleResult result)
    {
        if (!result.IsSuccess)
        {
            return Task.FromResult(RpcResponse.Fail(result.ErrorMessage, result.Args));
        }

        var recipe = result.Result as ProcessRecipeData;
        return Task.FromResult(recipe is null ? RpcResponse.Ok() : RpcResponse.Ok(JsonHelper.Serialize(ToDto(recipe))));
    }

    private static ProcessRecipeDto ToDto(ProcessRecipeData data)
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
    /// 步骤的一个字段给界面：下拉带上 sc.xml 里按站点 Type 分开写的选项。
    /// </summary>
    private static ProcessRecipeFieldDto ToDto(ProcessRecipeField field)
    {
        var dto = ToFieldDto(field);
        dto.OptionsByType = field.OptionsByType.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
        return dto;
    }

    /// <summary>
    /// 配方头的一项给界面：程序算的标出来；Type 的选项是腔体配的几种 Type，自己加的下拉是 sc.xml 里写的那一套。
    /// Type 必不必填看腔体：有腔体没配 Type（空着也是一种），Type 就能空着，跟后端保存时一样。
    /// </summary>
    private static ProcessRecipeFieldDto ToHeadDto(ProcessRecipeField field, IReadOnlyList<string> stationTypes)
    {
        bool isType = string.Equals(field.Key, ProcessRecipeHead.TypeKey, StringComparison.OrdinalIgnoreCase);
        var dto = ToFieldDto(field);
        dto.IsAuto = ProcessRecipeHead.IsAuto(field.Key);
        dto.Options = (isType ? stationTypes : field.OptionsFor(string.Empty)).ToList();
        if (isType)
        {
            dto.Required = !stationTypes.Contains(string.Empty);
        }

        return dto;
    }

    private static ProcessRecipeFieldDto ToFieldDto(ProcessRecipeField field)
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

    /// <summary>ON-LINE REMOTE 时 Host 锁着本地改配方（sc.xml Eap.Recipe 的 LockLocalEditInRemote）：返回拒绝的回包；没锁返回 null。</summary>
    private static Task<RpcResponse>? LockedByHost()
    {
        return E30RecipeComponent.Current?.IsLocalEditLocked == true
            ? Task.FromResult(RpcResponse.Fail(ErrorCodes.RecipeLockedByHost, []))
            : null;
    }
}
