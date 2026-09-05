namespace xyz.Client.Io.Models;

/// <summary>
/// IO 点位模型。
/// </summary>
public class IoPointModel
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public bool IsOn { get; set; }

    public bool IsOutput { get; set; }

    /// <summary>
    /// 平台画布上的 X 坐标。
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// 平台画布上的 Y 坐标。
    /// </summary>
    public double Y { get; set; }
}
