using xyz.Components;
using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// RFID 读头组件：承载读头品牌和串口装机配置。
/// </summary>
[Component(description: "RFID 读头组件")]
public class RfidReaderComponent : ComponentBase
{
    #region SC 装机常量

    [SCEditor("", "RFID", "RFID 读头品牌")]
    public string Brand { get; set; } = string.Empty;

    [SCEditor("COM", "RFID", "RFID 读头串口名称")]
    public string PortName { get; set; } = string.Empty;

    [SCEditor("9600", "RFID", "RFID 读头串口波特率")]
    public int BaudRate { get; set; } = 9600;

    [SCEditor("None", "RFID", "RFID 读头串口校验位")]
    public string Parity { get; set; } = "None";

    [SCEditor("8", "RFID", "RFID 读头串口数据位")]
    public int DataBits { get; set; } = 8;

    [SCEditor("One", "RFID", "RFID 读头串口停止位")]
    public string StopBits { get; set; } = "One";

    #endregion

    #region Action Template

    /// <summary>
    /// 读取载具 ID。公共入口固定，具体 RFID 协议由实现类重写。
    /// </summary>
    public string? ReadCarrierId()
    {
        return ReadCarrierIdCore();
    }

    protected virtual string? ReadCarrierIdCore()
    {
        throw new NotSupportedException(
            $"{GetType().Name} 尚未实现 RFID ReadCarrierId 指令。");
    }

    #endregion

    public RfidReaderComponent()
    {
        Name = "RFID";
        FullPath = "RFID";
    }
}
