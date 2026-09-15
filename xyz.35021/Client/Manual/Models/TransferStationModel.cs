namespace xyz._35021.Client.Manual.Models;

/// <summary>
/// 调度界面上的站点：站点名、机械手对准它的转台角度，以及卡片在搬运地图上的位置。
/// </summary>
public class TransferStationModel
{
    public TransferStationModel(string name, double angle, double left, double top)
    {
        Name = name;
        Angle = angle;
        Left = left;
        Top = top;
    }

    /// <summary>
    /// 站点名，与机械手站点表一致，取放片请求直接用它。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 机械手对准该站点时的转台角度（度，0 = 正上方，顺时针），与 Robot 控件的 Rotation 同一口径。
    /// </summary>
    public double Angle { get; }

    public double Left { get; }

    public double Top { get; }
}
