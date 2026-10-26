using xyz.Components.Attributes;
using xyz.Components.Enums;

using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// 喷嘴组件
/// </summary>
[Component(description: "喷嘴组件 (一路药液的通断阀，NC / NO 可选；流量开关挂子节点 Feedback、流量计挂子节点 AiSensorComponent，可选流量设定 AO)")]
public class NozzleComponent : ValveComponent, INozzle
{
    #region Machine 装机常量

    [Machine("", "Nozzle", "这一路的药液名 (DIW/SC1/HF...)，配方按它选喷嘴", Required = true)]
    public string Chemical { get; set; } = string.Empty;

    [Machine("-1", "IO", "流量设定 AO 索引 (-1 = 没接流量设定，配方里的流量不下发，只给流量计当监控目标)")]
    public int AoIndex { get; set; } = -1;

    [Machine("True", "Nozzle", "停液关阀后流量清不清零（True：AO 设定和流量计目标都设 0；False：保持上一步的流量）")]
    public bool ClearFlowOnStop { get; set; } = true;

    #endregion

    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "0", "相对示教喷嘴的轴偏移：摆臂示教位是对着先到中心的喷嘴示教的，这个喷嘴要喷到同一点轴还得多走多少就填多少（现场测）；示教的那个喷嘴填 0")]
    public double Offset
    {
        get { return GetEcDouble(nameof(Offset)); }
        set { SetEcDouble(nameof(Offset), value); }
    }

    #endregion

    #region 流量计（machine.xml 里喷嘴节点下面挂的）

    /// <summary>
    /// 这一路的流量计：喷嘴下面挂的 AI 组件，按 <see cref="IAiSensor"/> 认；没挂为 null（这一路没流量计，不监控、不显示流量）。
    /// </summary>
    public IAiSensor? FlowMeter
    {
        get
        {
            var meters = Children.OfType<IAiSensor>().ToList();
            if (meters.Count > 1)
            {
                throw new InvalidOperationException($"{FullPath}：喷嘴下面只能挂一个流量计（AI 组件）");
            }

            return meters.FirstOrDefault();
        }
    }

    /// <summary>
    /// 初始化时先认一遍流量计：挂多了开机就报。
    /// </summary>
    public override bool InitComponent()
    {
        _ = FlowMeter;
        return base.InitComponent();
    }

    #endregion

    #region 动作（返回 true 只表示写进 PLC 了）

    /// <summary>
    /// 接了流量设定 AO：没接的配方里填了流量也不下发，只给流量计当监控目标。
    /// </summary>
    public bool HasFlowControl => AoIndex >= 0;

    /// <summary>
    /// 设置流量
    /// </summary>
    /// <param name="litersPerMinute"></param>
    /// <returns></returns>
    public bool SetFlow(double litersPerMinute)
    {
        var meter = FlowMeter;
        if (meter is not null)
        {
            meter.SetTarget(litersPerMinute);
        }

        if (!HasFlowControl)
        {
            return true;
        }

        var io = Io;
        return io is not null && io.WriteAo(AoIndex, litersPerMinute).IsSuccess;
    }

    /// <summary>
    /// 停液：关阀；machine.xml ClearFlowOnStop 开着的再把流量清零。
    /// </summary>
    public bool Stop()
    {
        if (!Close())
        {
            return false;
        }

        if (ClearFlowOnStop)
        {
            return SetFlow(0);
        }

        return true;
    }

    #endregion
}
