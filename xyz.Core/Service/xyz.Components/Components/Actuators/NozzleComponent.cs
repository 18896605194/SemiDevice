using xyz.Components.Attributes;
using xyz.Components.Enums;

using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// 喷嘴组件：一路药液。DoIndex 开关阀（继承阀组件）；DiIndex 接流量开关，开阀后等它到位，没接填 -1；
/// AoIndex 给流量设定，没接填 -1。流量计不写在这里：sc 里在喷嘴下面挂一个 AiSensorComponent（监控使能 DO 填这个阀的 DO），
/// 上下限、开阀后延时、报警都用它的。
/// </summary>
[Component(description: "喷嘴组件 (一路药液的通断阀，可选流量开关 DI、流量设定 AO；流量计挂子节点 AiSensorComponent)")]
public class NozzleComponent : ValveComponent, INozzle
{
    #region SC 装机常量

    [SCEditor("", "Nozzle", "这一路的药液名 (DIW/SC1/HF...)，配方按它选喷嘴", Required = true)]
    public string Chemical { get; set; } = string.Empty;

    [SCEditor("-1", "IO", "流量设定 AO 索引 (-1 = 没接流量设定，配方里的流量不下发)")]
    public int AoIndex { get; set; } = -1;

    #endregion

    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "0", "相对示教喷嘴的轴偏移：摆臂示教位是对着先到中心的喷嘴示教的，这个喷嘴要喷到同一点轴还得多走多少就填多少（现场测）；示教的那个喷嘴填 0")]
    public double Offset
    {
        get { return GetEcDouble(nameof(Offset)); }
        set { SetEcDouble(nameof(Offset), value); }
    }

    #endregion

    #region 动作（返回 true 只表示写进 PLC 了）

    /// <summary>
    /// 接了流量设定 AO：没接的配方里填了流量也不下发。
    /// </summary>
    public bool HasFlowControl => AoIndex >= 0;

    /// <summary>
    /// 流量设定写 AO（L/min）；没接 AO 回 false。
    /// </summary>
    /// <param name="litersPerMinute"></param>
    /// <returns></returns>
    public bool SetFlow(double litersPerMinute)
    {
        var io = IoComponent.Current;
        if (io is null || !HasFlowControl)
        {
            return false;
        }

        return io.WriteAo(AoIndex, litersPerMinute);
    }

    /// <summary>
    /// 停液：关阀，接了 AO 的再把流量设定清零（下回开阀不带着上一步的流量）。
    /// </summary>
    public bool Stop()
    {
        if (!Close())
        {
            return false;
        }

        return !HasFlowControl || SetFlow(0);
    }

    #endregion
}
