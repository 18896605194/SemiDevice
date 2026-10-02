using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 喷嘴组件：一路药液的通断阀——IO 点位与到位判定都在 <see cref="OneStateComponent"/>，
/// 比阀门多的只是药液标识。
/// 配方按药液名找喷嘴（腔体 FindChildren&lt;NozzleComponent&gt;() 里按 Chemical 匹配），
/// 不认 DO 索引也不认节点名——管路改接线只改 sc.xml，配方不动。
/// </summary>
[Component(description: "喷嘴组件 (一路药液的通断阀)")]
public class NozzleComponent : ValveComponent
{
    #region SC 装机常量

    [SCEditor("", "Nozzle", "这一路的药液名 (DIW/SC1/HF...)，配方按它选喷嘴", Required = true)]
    public string Chemical { get; set; } = string.Empty;

    #endregion
}
