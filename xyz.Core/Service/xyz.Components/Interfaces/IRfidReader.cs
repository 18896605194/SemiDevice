namespace xyz.Components.Interfaces;

/// <summary>
/// RFID 读头能力契约：LoadPort 基类只依赖本接口，不同机型可换不同品牌的读头实现。
/// 实现类同时应是 RfidReaderComponent 子类（挂到模块组件树上承载 SC 装机配置）。
/// </summary>
public interface IRfidReader
{
    /// <summary>
    /// 打开读头连接（串口/网口）；由所属模块 Open 时调用。
    /// </summary>
    bool Open();

    /// <summary>
    /// 关闭读头连接，释放串口/网口资源。
    /// </summary>
    void Close();

    /// <summary>
    /// 读取载具 ID。
    /// </summary>
    string? ReadCarrierId();
}
