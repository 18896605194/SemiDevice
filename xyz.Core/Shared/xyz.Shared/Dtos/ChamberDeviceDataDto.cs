namespace xyz.Shared.Dtos;

/// <summary>
/// 一个腔体里各设备的实时状态（腔体手动页的轴页签、气缸表、三维图都从这里取），结构跟 sc.xml 一样：
/// 门、Bowl、卡盘，和各条摆臂——摆臂下面是它的升降（Lift）和喷嘴。sc 里加了 Arm2、Bowl2、喷嘴，这里自动多一项，推送和界面都不用改。
/// 门、Bowl、卡盘、摆臂以外的设备不在里面（35021 没有，真有了再加一个位置）。
/// 有变化才推、留存（token 是模块名）；跟模块状态 ChamberDto 分开推：摆臂一动就要推，别让只关心模块状态的界面跟着刷。
/// </summary>
public class ChamberDeviceDataDto
{
    /// <summary>模块实例名，与 EventBus token 一致，如 "Chamber1"。</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>腔门；sc 里没配为 null。</summary>
    public ChamberCylinderDto? Door { get; set; }

    /// <summary>Bowl（几层都在，按 sc 的先后；三维图只画第一个）。</summary>
    public List<ChamberCylinderDto> Bowls { get; set; } = [];

    /// <summary>卡盘；sc 里没配为 null。</summary>
    public ChamberSpinDto? Spin { get; set; }

    /// <summary>各条摆臂，按 sc 的先后。</summary>
    public List<ChamberArmDto> Arms { get; set; } = [];

    /// <summary>跟上一次推的比有没有变化（组成或任何一个值）；没有上一次视为变化。</summary>
    public bool HasStateChanged(ChamberDeviceDataDto? previous)
    {
        return previous is null
            || Module != previous.Module
            || !Equals(Door, previous.Door)
            || !Bowls.SequenceEqual(previous.Bowls)
            || !Equals(Spin, previous.Spin)
            || !Arms.SequenceEqual(previous.Arms);
    }
}

/// <summary>
/// 一根轴：位置、速度、五盏灯。位置、速度按 3 位小数取整，编码器在这一位以下抖不会一直推。
/// </summary>
public record ChamberAxisDto
{
    /// <summary>组件全路径（sc.xml 的组件路径），如 "Chamber1.Arm1"；轴动作按它找轴。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>PLC 状态有效；false 时位置、速度是断线前的旧值，界面显示"—"。</summary>
    public bool HasPlcData { get; set; }

    /// <summary>实际位置。</summary>
    public double CurrentPosition { get; set; }

    /// <summary>实际速度（卡盘正负是转向）。</summary>
    public double CurrentSpeed { get; set; }

    /// <summary>伺服使能（伺服就绪）。</summary>
    public bool IsServoOn { get; set; }

    /// <summary>已回零。</summary>
    public bool IsHomed { get; set; }

    /// <summary>运动中（PLC 忙）。</summary>
    public bool IsBusy { get; set; }

    /// <summary>已到位。</summary>
    public bool IsInPosition { get; set; }

    /// <summary>驱动器报错（故障）。</summary>
    public bool IsError { get; set; }
}

/// <summary>
/// 卡盘：轴的状态 + 在不在转。
/// </summary>
public record ChamberSpinDto : ChamberAxisDto
{
    /// <summary>在转（三维图转盘面，转向看 CurrentSpeed 的正负）。</summary>
    public bool IsSpinning { get; set; }
}

/// <summary>
/// 一条摆臂：摆动轴的状态 + 摆到哪，以及装在它上面的升降（Lift）和喷嘴（跟 sc 里挂在它下面的一样）。
/// </summary>
public record ChamberArmDto : ChamberAxisDto
{
    /// <summary>摆到哪：0 = Home，1 = 工艺位（Wafer 中心）。</summary>
    public double Reach { get; set; }

    /// <summary>第一个边缘在 Reach 上的位置（0 = 没示教）。</summary>
    public double EdgeReach { get; set; }

    /// <summary>这条臂的升降气缸；sc 里没配为 null。</summary>
    public ChamberCylinderDto? Lift { get; set; }

    /// <summary>这条臂上的喷嘴，按 sc 的先后。</summary>
    public List<ChamberNozzleDto> Nozzles { get; set; } = [];

    /// <summary>喷嘴表按内容比（记录默认只比列表引用），其余照记录的规矩逐项比。</summary>
    public virtual bool Equals(ChamberArmDto? other)
    {
        return other is not null
            && base.Equals(other)
            && Reach == other.Reach
            && EdgeReach == other.EdgeReach
            && Equals(Lift, other.Lift)
            && Nozzles.SequenceEqual(other.Nozzles);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(base.GetHashCode(), Reach, EdgeReach, Lift, Nozzles.Count);
    }
}

/// <summary>
/// 一个双作用气缸（门、Bowl、Lift）：在哪一侧。
/// </summary>
public record ChamberCylinderDto
{
    /// <summary>组件全路径，如 "Chamber1.Arm1.Lift"；气缸动作按它找气缸。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>在哪一侧：升到位（开侧）、降到位（关侧）、未知。</summary>
    public CylinderPosition Position { get; set; }
}

/// <summary>
/// 一路喷嘴：在不在出液。
/// </summary>
public record ChamberNozzleDto
{
    /// <summary>组件全路径，如 "Chamber1.Arm1.Nozzle_DIW"。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>药液名，照 sc 原样。</summary>
    public string Chemical { get; set; } = string.Empty;

    /// <summary>在出液：接了流量开关看反馈，没接看阀的输出。</summary>
    public bool IsOn { get; set; }
}

/// <summary>
/// 双作用气缸在哪一侧。
/// </summary>
public enum CylinderPosition
{
    /// <summary>未知：命令发了、到位信号还没亮（还在走），或者两侧信号都没亮、PLC 没连上。</summary>
    Unknown,

    /// <summary>开到位：门开、Bowl 升、Lift 升。</summary>
    Opened,

    /// <summary>关到位：门关、Bowl 降、Lift 降。</summary>
    Closed,
}
