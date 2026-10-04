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
/// 检查都在库里做（编号、名称、版本、步骤），这里只管取库、转 DTO、带上操作人。
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
            Arms = library.Arms.Select(arm => new ProcessArmDto { Name = arm.Name, Chemicals = arm.Chemicals.ToList() }).ToList(),
            MinSeconds = ProcessRecipeComponent.MinPositiveValue,
            MaxSeconds = library.MaxStepSeconds,
            MaxRpm = library.MaxRpm,
            MinFlow = ProcessRecipeComponent.MinPositiveValue,
            MaxFlow = library.MaxFlow,
            MinPosition = ArmAxisComponent.WaferEdgePosition,
            MaxPosition = ArmAxisComponent.WaferCenterPosition,
            MinScanSpeed = ProcessRecipeComponent.MinPositiveValue,
            MaxScanSpeed = library.MaxScanSpeed,
            MaxTotalSeconds = library.MaxTotalSeconds,
            NewStepSeconds = ProcessRecipeComponent.NewStepSeconds,
            NewStepRpm = ProcessRecipeComponent.NewStepRpm,
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
            Seconds = step.Seconds,
            Rpm = step.Rpm,
            Arm = step.Arm ?? string.Empty,
            Chemical = step.Chemical ?? string.Empty,
            Flow = step.Flow,
            Mode = step.Mode,
            Position = step.Position,
            ScanTo = step.ScanTo,
            ScanSpeed = step.ScanSpeed,
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
            Steps = data.Steps.Select(step => new ProcessRecipeStepDto
            {
                Seconds = step.Seconds,
                Rpm = step.Rpm,
                Arm = step.Arm,
                Chemical = step.Chemical,
                Flow = step.Flow,
                Mode = step.Mode,
                Position = step.Position,
                ScanTo = step.ScanTo,
                ScanSpeed = step.ScanSpeed,
            }).ToList(),
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
