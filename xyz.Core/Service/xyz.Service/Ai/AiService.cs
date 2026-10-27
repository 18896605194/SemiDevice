using ProtoBuf.Grpc;
using xyz.Elements.Interfaces;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Service.Ai;

/// <summary>
/// AI 助手服务
/// </summary>
public class AiService : BaseService, IAiService
{
    private readonly IAi? _ai;

    private readonly ISystemModule? _system;

    private readonly IMapper _mapper;

    public AiService()
    {
        _ai = IocHelper.GetService<IAi>();
        _system = IocHelper.GetService<ISystemModule>();
        _mapper = IocHelper.GetService<IMapper>() ?? throw new InvalidOperationException("machine.xml 没配映射元件（Mapper）");
    }

    /// <summary>
    /// 获取ai服务状态
    /// </summary>
    /// <param name="request"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public Task<RpcResponse> GetStatusAsync(RpcRequest request, CallContext context = default)
    {
        if (_ai is null)
        {
            return NotInstalled(ErrorCodes.AiNotInstalled);
        }

        return Task.FromResult(RpcResponse.Ok(JsonHelper.Serialize(new AiStatusDto { Model = _ai.Model })));
    }

    /// <summary>
    /// 解释一条报警
    /// </summary>
    public async Task<RpcResponse> ExplainAlarmAsync(RpcRequest request, CallContext context = default)
    {
        if (_ai is null)
        {
            return await NotInstalled(ErrorCodes.AiNotInstalled);
        }

        if (!request.TryGet<string>(RpcParams.Source, out var source)|| !request.TryGet<string>(RpcParams.Code, out var code))
        {
            return await InvalidRequest();
        }

        string language = _system?.Language ?? string.Empty;
        var result = await _ai.ExplainAlarmAsync(source, code, language, context.CancellationToken);
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
