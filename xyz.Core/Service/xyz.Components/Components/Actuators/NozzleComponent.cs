using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 喷嘴组件：一路药液的通断阀——IO 点位与到位判定都在 <see cref="OneStateComponent"/>，
/// 比阀门多的是药液标识和流量设定（AO，可选）。
/// 配方按药液名找喷嘴（腔体按摆臂下喷嘴的 Chemical 匹配），
/// 不认 DO 索引也不认节点名——管路改接线只改 sc.xml，配方不动。
/// </summary>
[Component(description: "喷嘴组件 (一路药液的通断阀，可选流量设定 AO)")]
public class NozzleComponent : ValveComponent
{
    #region SC 装机常量

    [SCEditor("", "Nozzle", "这一路的药液名 (DIW/SC1/HF...)，配方按它选喷嘴", Required = true)]
    public string Chemical { get; set; } = string.Empty;

    [SCEditor("-1", "IO", "流量设定 AO 索引 (-1 = 没接流量设定，配方里的流量不下发)")]
    public int FlowAoIndex { get; set; } = -1;

    #endregion

    #region 动作（返回 true 只表示写进 PLC 了）

    /// <summary>接了流量设定 AO。</summary>
    public bool HasFlowControl => FlowAoIndex >= 0;

    /// <summary>下发流量设定（L/min，按点表换成 AO 原始值）；没接 AO、PLC 没连返回 false。写完即完成，不改 ActionState。</summary>
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
    /// 停液：关阀，接了流量设定的把设定也清零——不留旧设定，下一次开阀前按配方重新给。
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
