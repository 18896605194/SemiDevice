using xyz.Components.Attributes;

using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// 喷嘴组件
/// </summary>
[Component(description: "喷嘴组件 (一路药液的通断阀，可选流量设定 AO)")]
public class NozzleComponent : ValveComponent, INozzle
{
    #region SC 装机常量

    [SCEditor("", "Nozzle", "这一路的药液名 (DIW/SC1/HF...)，配方按它选喷嘴", Required = true)]
    public string Chemical { get; set; } = string.Empty;

    [SCEditor("-1", "IO", "流量设定 AO 索引 (-1 = 没接流量设定，配方里的流量不下发)")]
    public int FlowAoIndex { get; set; } = -1;

    #endregion

    #region 动作（返回 true 只表示写进 PLC 了）

    public bool HasFlowControl => FlowAoIndex >= 0;

    /// <summary>
    /// 流量设定
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

        return io.WriteAo(FlowAoIndex, litersPerMinute);
    }

    /// <summary>
    /// 关阀
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
