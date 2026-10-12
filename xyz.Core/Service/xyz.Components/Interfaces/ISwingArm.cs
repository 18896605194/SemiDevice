namespace xyz.Components.Interfaces;

/// <summary>腔体摆臂：轴、升降、药液及配方坐标换算由组件一起提供。</summary>
public interface ISwingArm : IAxis
{
    /// <summary>这条臂上的升降部件；没装时为空。</summary>
    IChamberLift? Lift { get; }

    /// <summary>这条臂上的全部药液喷嘴。</summary>
    IReadOnlyList<INozzle> Nozzles { get; }

    /// <summary>按药液名找喷嘴。</summary>
    INozzle? FindNozzle(string chemical);

    /// <summary>晶圆坐标换算成实际轴位置。</summary>
    double ToAxisPosition(double waferPosition);

    /// <summary>晶圆坐标中的速度换算成轴速度。</summary>
    double ToAxisSpeed(double waferSpeed);

    /// <summary>供设备图显示的当前摆幅。</summary>
    double Reach { get; }

    /// <summary>晶圆边缘在摆幅里的位置。</summary>
    double EdgeReach { get; }
}

