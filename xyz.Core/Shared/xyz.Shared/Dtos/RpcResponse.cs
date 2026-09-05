using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 通用 RPC 响应，返回数据统一使用 JSON 字符串。
/// </summary>
[ProtoContract]
public class RpcResponse
{
    /// <summary>
    /// 是否成功。
    /// </summary>
    [ProtoMember(1)]
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息/提示。
    /// </summary>
    [ProtoMember(2)]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 返回数据，JSON 字符串。
    /// </summary>
    [ProtoMember(3)]
    public string Data { get; set; } = "{}";

    /// <summary>
    /// 错误码（见 xyz.Shared.Errors.ErrorCodes 及各机型 ErrorCodes）。
    /// 前端以码查语言包渲染当前语言的句子；为空表示无码（如老接口）。
    /// </summary>
    [ProtoMember(4)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 错误码参数，顺序见错误码定义的注释；供前端句子模板占位。
    /// </summary>
    [ProtoMember(5)]
    public List<string> Args { get; set; } = [];

    /// <summary>
    /// 成功响应（无数据）。
    /// </summary>
    public static RpcResponse Ok()
    {
        return new RpcResponse
        {
            Success = true,
            Message = "ok"
        };
    }

    /// <summary>
    /// 成功响应（带数据）。
    /// </summary>
    public static RpcResponse Ok(string data)
    {
        return new RpcResponse
        {
            Success = true,
            Message = "ok",
            Data = data
        };
    }

    /// <summary>
    /// 失败响应。
    /// </summary>
    public static RpcResponse Fail(string message)
    {
        return new RpcResponse
        {
            Success = false,
            Message = message,
            Data = "{}"
        };
    }

    /// <summary>
    /// 失败响应（带错误码，无句子）：界面按 Code 查语言包渲染。
    /// Message 自动填码本身（纯调试可读），中文调试信息走 LogHelper.Debug。
    /// </summary>
    public static RpcResponse Fail(string code, IEnumerable<string>? args = null, string? debugMessage = null)
    {
        return new RpcResponse
        {
            Success = false,
            Message = debugMessage ?? code,
            Data = "{}",
            Code = code,
            Args = args?.ToList() ?? []
        };
    }
}
