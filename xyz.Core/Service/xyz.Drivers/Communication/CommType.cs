namespace xyz.Drivers.Communication;

/// <summary>
/// 通讯传输类型（SC 装机配置选择）。
/// </summary>
public enum CommType
{
    /// <summary>
    /// 串口（RS232）。
    /// </summary>
    Serial,

    /// <summary>
    /// 网口（TCP Client）。
    /// </summary>
    Tcp,
}
