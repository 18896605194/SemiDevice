namespace xyz.Shared.Dtos;

/// <summary>腔体外观：Xaml 供页面显示，Xml 保留旧版布局兼容。实时状态来自 ChamberDeviceDataDto。</summary>
public sealed class ChamberLayoutDto
{
    public string Module { get; set; } = string.Empty;
    public string ComponentRoot { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
    public string Xaml { get; set; } = string.Empty;
}
