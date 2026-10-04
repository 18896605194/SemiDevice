using System.Xml.Serialization;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 工艺配方的一步（存成文件的样子）：时间、转速；摆臂（空 = 这一步不出液，摆臂在 Home）、药液、流量、方式（Time / Scan）、位置。
/// 位置是晶圆坐标（0 = 从 Home 摆过去先到的晶圆边缘，150 = 晶圆中心）。
/// 用不上的字段不写进文件：不出液的不写药液、流量、方式、位置；Time 不写 Scan 的另一头和速度——文件打开一眼看得清。
/// </summary>
public class ProcessRecipeStep
{
    /// <summary>
    /// 这一步多长，秒。
    /// </summary>
    [XmlAttribute]
    public double Seconds { get; set; }

    /// <summary>
    /// 转速 rpm（0 = 停着泡）。
    /// </summary>
    [XmlAttribute]
    public int Rpm { get; set; }

    /// <summary>
    /// 摆臂名（sc.xml 里腔体下的摆臂轴名）；空 = 不出液。
    /// </summary>
    [XmlAttribute]
    public string Arm { get; set; } = string.Empty;

    /// <summary>
    /// 药液（这条摆臂上喷嘴的 Chemical）。
    /// </summary>
    [XmlAttribute]
    public string Chemical { get; set; } = string.Empty;

    /// <summary>
    /// 流量 L/min。
    /// </summary>
    [XmlAttribute]
    public double Flow { get; set; }

    [XmlAttribute]
    public ProcessArmMode Mode { get; set; }

    /// <summary>
    /// 位置：Time 停在这里喷，Scan 从这里扫到 <see cref="ScanTo"/>。
    /// </summary>
    [XmlAttribute]
    public double Position { get; set; }

    /// <summary>
    /// Scan 的另一头。
    /// </summary>
    [XmlAttribute]
    public double ScanTo { get; set; }

    /// <summary>
    /// Scan 的速度 mm/s。
    /// </summary>
    [XmlAttribute]
    public double ScanSpeed { get; set; }

    private bool IsDispensing => Arm.Length > 0;

    private bool IsScan => IsDispensing && Mode == ProcessArmMode.Scan;

    // XmlSerializer 按 ShouldSerialize + 属性名决定写不写这个字段

    public bool ShouldSerializeArm() => IsDispensing;

    public bool ShouldSerializeChemical() => IsDispensing;

    public bool ShouldSerializeFlow() => IsDispensing;

    public bool ShouldSerializeMode() => IsDispensing;

    public bool ShouldSerializePosition() => IsDispensing;

    public bool ShouldSerializeScanTo() => IsScan;

    public bool ShouldSerializeScanSpeed() => IsScan;

    public ProcessRecipeStep Clone()
    {
        return (ProcessRecipeStep)MemberwiseClone();
    }
}
