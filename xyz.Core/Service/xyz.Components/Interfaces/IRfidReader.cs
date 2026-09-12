namespace xyz.Components.Interfaces;

/// <summary>
/// RFID 读头能力契约：LoadPort 基类只依赖本接口，不同机型可换不同品牌的读头实现。
/// 实现类不限制基类；需要从 sc.xml 装机时实现为 ComponentBase 子类并命名 RFID（模块会挂到组件树灌值）。
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
