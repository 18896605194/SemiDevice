namespace xyz.Elements.Interfaces;

/// <summary>
/// gRPC 服务地址
/// </summary>
public interface IRpc
{
    /// <summary>
    /// 监听端口
    /// </summary>
    int Port { get; }

    /// <summary>
    /// 监听地址（localhost 或明确的 IP）
    /// </summary>
    string Host { get; }

    /// <summary>
    /// 单条消息最大多少 MB（后端收的上限）
    /// </summary>
    int MaxMessageSizeMb { get; }
}
