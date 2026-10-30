namespace xyz.Shared.Dtos;

/// <summary>外观配置；设备清单和反馈仍来自 ChamberDeviceDataDto。空 Xml 表示未配置布局。</summary>
public sealed class ChamberLayoutDto
{
    public string Module { get; set; } = string.Empty;
    public string ComponentRoot { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
}
