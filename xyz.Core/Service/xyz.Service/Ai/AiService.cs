using ProtoBuf.Grpc;
using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Ai;

/// <summary>
/// AI 助手 gRPC 服务：转给 AI 组件（IAiComponent），回答用的语言跟界面一样（整机 sc 的 Language）；没配 Ai 节点回 ai.not_installed。
/// </summary>
public class AiService : IAiService
{
    private readonly IAiComponent? _ai;

    private readonly ISystemModule? _system;

    private readonly IMapperComponent _mapper;

    public AiService()
    {
        _ai = IocHelper.GetService<IAiComponent>();
        _system = IocHelper.GetService<ISystemModule>();
        _mapper = IocHelper.GetService<IMapperComponent>() ?? throw new InvalidOperationException("sc.xml 没配映射组件（Mapper）");
    }

    public Task<RpcResponse> GetStatusAsync(RpcRequest request, CallContext context = default)
    {
        var ai = _ai;
        if (ai is null)
        {
            return Task.FromResult(RpcResponse.Fail(ErrorCodes.AiNotInstalled, []));
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(new AiStatusDto { Model = ai.Model })));
    }

    /// <summary>
    /// 解释一条当前报警：组件排队、整理材料、问模型；没做成原样回组件给的错误码
    /// </summary>
    public async Task<RpcResponse> ExplainAlarmAsync(AiAlarmRequest request, CallContext context = default)
    {
        var ai = _ai;
        if (ai is null)
        {
            return RpcResponse.Fail(ErrorCodes.AiNotInstalled, []);
        }

        string language = _system?.Language ?? string.Empty;
        var result = await ai.ExplainAlarmAsync(request.Source, request.Code, language, context.CancellationToken);
        if (!result.IsSuccess)
        {
            return RpcResponse.Fail(result.ErrorMessage, result.Args);
        }

        var answer = result.Result;
        if (answer is null)
        {
            return RpcResponse.Fail(ErrorCodes.AiEmptyAnswer, []);
        }

        return RpcResponse.Ok(JsonHelper.Serialize(_mapper.Map<AiAnswerDto>(answer)));
    }
}
