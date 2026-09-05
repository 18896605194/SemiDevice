namespace RejeRobotSimulator.Protocol;

/// <summary>
/// 回复构建器：按照协议格式 ">StatusCode#Content@CommandName;" 构建回复
/// </summary>
public static class ResponseBuilder
{
    public const string SUCCESS_CODE = "00000000";

    /// <summary>
    /// 构建第一次回复（确认收到）
    /// </summary>
    public static string BuildAck()
    {
        return ">;";
    }

    /// <summary>
    /// 构建成功回复
    /// </summary>
    public static string BuildSuccess(string commandName, string content = "OK")
    {
        return $">{SUCCESS_CODE}#{content}@{commandName};";
    }

    /// <summary>
    /// 构建失败回复
    /// </summary>
    public static string BuildFailure(string commandName, string errorCode, string errorDesc)
    {
        return $">{errorCode}#{errorDesc}@{commandName};";
    }

    /// <summary>
    /// 构建事件推送，固定 @Event 后缀。
    /// 例：BuildEvent("SubWaferEx,1,0") -> ">00000000#SubWaferEx,1,0@Event;"
    /// </summary>
    public static string BuildEvent(string content)
    {
        return $">{SUCCESS_CODE}#{content}@Event;";
    }
}
