using xyz.Shared.Dtos;

namespace xyz.Client.Manual.Models;

/// <summary>转台图四周的站点角标：设备站点号 + 名称 + 所在方位。</summary>
public sealed class RobotStationModel
{
    public string Name { get; init; } = string.Empty;

    /// <summary>设备站点号（sc.xml Number，如 LoadPort1=1、Chamber1=3）。</summary>
    public int Number { get; init; }

    /// <summary>机械手伸出方向（sc.xml Direction）。</summary>
    public RobotDirection Direction { get; init; }

    /// <summary>机械手伸出距离（sc.xml Y，数值）。</summary>
    public double Y { get; init; }

    /// <summary>角标主文案：站点号。</summary>
    public string NumberText => Number.ToString();

    /// <summary>角标副文案：站点名。</summary>
    public string Title => Name;
}
