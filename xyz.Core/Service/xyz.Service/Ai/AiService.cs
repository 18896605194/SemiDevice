using ProtoBuf.Grpc;
using xyz.Components.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Ai;

/// <summary>
/// AI 助手服务
/// </summary>
public class AiService : IAiService
{
    private readonly IAi? _ai;

    private readonly ISystemModule? _system;

    private readonly IMapper _mapper;

    public AiService()
    {
        _ai = IocHelper.GetService<IAi>();
        _system = IocHelper.GetService<ISystemModule>();
        _mapper = IocHelper.GetService<IMapper>() ?? throw new InvalidOperationException("machine.xml 没配映射组件（Mapper）");
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
    /// 解释一条报警
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
