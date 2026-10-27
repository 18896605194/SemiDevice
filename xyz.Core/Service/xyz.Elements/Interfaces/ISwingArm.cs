namespace xyz.Elements.Interfaces;

/// <summary>腔体摆臂：轴、升降、药液及配方坐标换算由元件一起提供。</summary>
public interface ISwingArm : IAxis
{
    /// <summary>这条臂上的升降部件；没装时为空。</summary>
    IChamberLift? Lift { get; }

    /// <summary>这条臂上的全部药液喷嘴。</summary>
    IReadOnlyList<INozzle> Nozzles { get; }

    /// <summary>按药液名找喷嘴。</summary>
    INozzle? FindNozzle(string chemical);

    /// <summary>晶圆坐标换算成实际轴位置；给了喷嘴就让这个喷嘴到那个点（加上它的偏移）。</summary>
    double ToAxisPosition(double waferPosition, INozzle? nozzle = null);

    /// <summary>供设备图显示的当前摆幅。</summary>
    double Reach { get; }

    /// <summary>晶圆边缘在摆幅里的位置。</summary>
    double EdgeReach { get; }
}

