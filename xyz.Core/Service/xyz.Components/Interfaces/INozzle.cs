namespace xyz.Components.Interfaces;

/// <summary>一路药液喷嘴：配方按药液名选择，执行通过接口控制。</summary>
public interface INozzle : IValve
{
    /// <summary>这一路药液的名称。</summary>
    string Chemical { get; }

    /// <summary>相对示教喷嘴的轴偏移：摆臂换算配方位置时加上它。</summary>
    double Offset { get; }

    /// <summary>喷嘴是否开启。</summary>
    bool IsOn { get; }

    /// <summary>是否提供流量设定。</summary>
    bool HasFlowControl { get; }

    /// <summary>给流量，单位 L/min：接了 AO 的下发，挂了流量计的当监控目标。</summary>
    bool SetFlow(double litersPerMinute);

    /// <summary>停液并清零流量设定。</summary>
    bool Stop();
}

