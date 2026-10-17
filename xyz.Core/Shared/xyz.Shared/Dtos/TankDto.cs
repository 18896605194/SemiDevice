namespace xyz.Shared.Dtos;

/// <summary>
/// 一个供液槽的液位、状态和它下面的液位信号（结构跟 sc 一样），token 是槽的全路径
/// </summary>
public class TankDto
{
    /// <summary>
    /// 槽的全路径，跟 EventBus token 一样，如 "CDS.IPA.Tank1"
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 液位，取值见后端 TankLevel
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 状态，取值见后端 TankState
    /// </summary>
    public int State { get; set; }

    /// <summary>
    /// LL 信号，sc 里没配是 null
    /// </summary>
    public DiSensorDto? LL { get; set; }

    /// <summary>
    /// L 信号，sc 里没配是 null
    /// </summary>
    public DiSensorDto? L { get; set; }

    /// <summary>
    /// H 信号，sc 里没配是 null
    /// </summary>
    public DiSensorDto? H { get; set; }

    /// <summary>
    /// HH 信号，sc 里没配是 null
    /// </summary>
    public DiSensorDto? HH { get; set; }

    /// <summary>
    /// Overfill 信号，sc 里没配是 null
    /// </summary>
    public DiSensorDto? Overfill { get; set; }
}
