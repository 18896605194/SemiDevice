namespace xyz.Modules;

/// <summary>
/// 腔体做工艺（<see cref="ChamberProcessOperation"/>）走到哪一段了
/// </summary>
public enum ChamberProcessPhase
{
    /// <summary>
    /// 准备：喷嘴全关、Lift 升
    /// </summary>
    Prepare,

    /// <summary>
    /// 准备：Bowl 升（围住盘面挡液）
    /// </summary>
    BowlUp,

    /// <summary>
    /// 换步：读这一步的字段，先关掉这一步不用的喷嘴
    /// </summary>
    StepStart,

    /// <summary>
    /// 这一步的转速给卡盘
    /// </summary>
    StepSpin,

    /// <summary>
    /// 别的摆臂回 Home、这一步的摆臂摆到位置
    /// </summary>
    StepArms,

    /// <summary>
    /// 给流量、开这一步的喷嘴
    /// </summary>
    StepNozzleOn,

    /// <summary>
    /// 喷：Time 停着喷，Scan 来回扫，到点进下一步
    /// </summary>
    StepDwell,

    /// <summary>
    /// 收尾：喷嘴全关
    /// </summary>
    EndNozzles,

    /// <summary>
    /// 收尾：摆臂回 Home
    /// </summary>
    EndArms,

    /// <summary>
    /// 收尾：卡盘停转
    /// </summary>
    EndSpin,

    /// <summary>
    /// 收尾：Bowl 降
    /// </summary>
    EndBowls,

    /// <summary>
    /// 做完
    /// </summary>
    Done,
}
