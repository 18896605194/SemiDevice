using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Io;
using xyz.Configs;

namespace xyz.Components.Components;

/// <summary>
/// IO 组件：点表 + 按索引读写。值不另存一份——读的时候直接从 PLC 组件最近一拍的整块缓存解出来，
/// AI/AO 按点表标定换算成工程值。界面看到的整包由 IoPublisher 按周期现读现拼。
/// </summary>
[Component(description: "IO 组件（点表 + 按索引读写）")]
public class IoComponent : ComponentBase
{
    /// <summary>
    /// 当前 IO 表；sc.xml 里装出来即生效，上层可按索引取点。
    /// </summary>
    public static IoComponent? Current { get; set; }

    public IoComponent()
    {
        Current = this;
    }

    #region SC

    [SCEditor("IO", "Io", "点表目录（相对 sc.xml 所在目录），里面放 DI.csv / DO.csv / AI.csv / AO.csv")]
    public string PointTableDirectory { get; set; } = "IO";

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "10000", "500", "IO 界面推送周期")]
    public int PublishIntervalMs
    {
        get { return GetEcInt(nameof(PublishIntervalMs)); }
        set { SetEcInt(nameof(PublishIntervalMs), value); }
    }

    #endregion

    #region SV

    /// <summary>
    /// 采集是否在工作（SV）：PLC 连着才算。断了以后所有点都读不到（TryRead 一律 false），不会拿陈旧值当真。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "IO 采集是否在工作")]
    public bool IsCollecting => PlcComponent.Current?.IsConnected ?? false;

    #endregion

    #region 点表

    public IoPointTable Di { get; } = new("DI");

    public IoPointTable Do { get; } = new("DO");

    public IoPointTable Ai { get; } = new("AI");

    public IoPointTable Ao { get; } = new("AO");

    /// <summary>
    /// 读点表；由装配在模块启动前调用。点表不在就是空表，照常空转不拦启动——
    /// 装机时电控还没给点表是常事。
    /// </summary>
    public bool Open()
    {
        // 全限定：组件基类自己有个 Path 属性（组件路径），这儿的 Path 不是它。
        string directory = System.IO.Path.Combine(SC.ConfigDirectory, PointTableDirectory);

        Di.Load(System.IO.Path.Combine(directory, "DI.csv"));
        Do.Load(System.IO.Path.Combine(directory, "DO.csv"));
        Ai.Load(System.IO.Path.Combine(directory, "AI.csv"));
        Ao.Load(System.IO.Path.Combine(directory, "AO.csv"));

        LogHelper.Info($"[{FullPath}] 点表装载：DI {Di.Points.Count}、DO {Do.Points.Count}、"
                       + $"AI {Ai.Points.Count}、AO {Ao.Points.Count} 点（{directory}）");
        return true;
    }

    #endregion

    #region 按索引读写（点表里没有该索引、PLC 没连或读不到时一律 false）

    /// <summary>
    /// 读一个 DI 点。
    /// </summary>
    public bool TryReadDi(int index, out bool on)
    {
        on = false;
        var point = Di.Find(index);
        var plc = PlcComponent.Current;
        if (point is null || plc is null)
        {
            return false;
        }

        return plc.TryReadDi(point.Index, out on);
    }

    /// <summary>
    /// 回读一个 DO 点当前的输出状态。
    /// </summary>
    public bool TryReadDo(int index, out bool on)
    {
        on = false;
        var point = Do.Find(index);
        var plc = PlcComponent.Current;
        if (point is null || plc is null)
        {
            return false;
        }

        return plc.TryReadDo(point.Index, out on);
    }

    /// <summary>
    /// 写一个 DO 点。
    /// </summary>
    public bool WriteDo(int index, bool on)
    {
        var point = Do.Find(index);
        var plc = PlcComponent.Current;
        if (point is null || plc is null)
        {
            return false;
        }

        return plc.WriteDo(point.Index, on);
    }

    /// <summary>
    /// 读一个 AI 点的工程值（按点表里的标定换算；没标定就是原始码）。
    /// </summary>
    public bool TryReadAi(int index, out double value)
    {
        value = 0;
        var point = Ai.Find(index);
        var plc = PlcComponent.Current;
        if (point is null || plc is null || !plc.TryReadAi(point.Index, out double raw))
        {
            return false;
        }

        value = point.ToEngineering(raw);
        return true;
    }

    /// <summary>
    /// 回读一个 AO 点当前的输出工程值。
    /// </summary>
    public bool TryReadAo(int index, out double value)
    {
        value = 0;
        var point = Ao.Find(index);
        var plc = PlcComponent.Current;
        if (point is null || plc is null || !plc.TryReadAo(point.Index, out double raw))
        {
            return false;
        }

        value = point.ToEngineering(raw);
        return true;
    }

    /// <summary>
    /// 写一个 AO 点：给的是工程值，按标定反算成原始码再下发。
    /// </summary>
    public bool WriteAo(int index, double engineering)
    {
        var point = Ao.Find(index);
        var plc = PlcComponent.Current;
        if (point is null || plc is null)
        {
            return false;
        }

        return plc.WriteAo(point.Index, point.ToRaw(engineering));
    }

    #endregion
}
