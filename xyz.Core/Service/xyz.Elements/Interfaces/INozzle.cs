namespace xyz.Elements.Interfaces;

/// <summary>
/// 药液喷嘴
/// </summary>
public interface INozzle : IValve
{
    /// <summary>
    /// 药液的名称
    /// </summary>
    string Chemical { get; }

    /// <summary>
    /// 喷嘴的轴偏移
    /// </summary>
    double Offset { get; }

    /// <summary>
    /// 喷嘴是否开启
    /// </summary>
    bool IsOn { get; }

    /// <summary>
    /// 是否提供流量设定
    /// </summary>
    bool HasFlowControl { get; }

    /// <summary>
    /// 设置流量
    /// </summary>
    /// <param name="litersPerMinute"></param>
    /// <returns></returns>
    bool SetFlow(double litersPerMinute);

    /// <summary>
    /// 停液；machine.xml 配了清零的流量设成 0
    /// </summary>
    /// <returns></returns>
    bool Stop();
}

