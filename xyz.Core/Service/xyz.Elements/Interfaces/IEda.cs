namespace xyz.Elements.Interfaces;

/// <summary>
/// EDA（Interface A）：给厂里 FDC / APC 这类系统采数据的 Web 服务
/// </summary>
public interface IEda
{
    /// <summary>
    /// 启没启用（machine.xml）
    /// </summary>
    bool IsEnable { get; }

    /// <summary>
    /// 在不在监听（启用了但端口被占这类起不来时为 false）
    /// </summary>
    bool IsListening { get; }

    /// <summary>
    /// 客户端先连的接口发现地址（http://主机:端口/E132InterfaceDiscovery）
    /// </summary>
    string DiscoveryAddress { get; }
}
