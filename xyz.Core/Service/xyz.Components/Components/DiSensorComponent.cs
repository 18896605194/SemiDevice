using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// DI 值监控组件：单个 DI，电平触发 + 报警防抖（EC DebounceMs）。
/// 公共组件：装进哪个模块都一样用，报警算在装它的模块头上（模块的 HasAlarm 自动包含）。
/// DI 经 IoComponent 按索引读（点表里得有这个索引）；读不到（PLC 没连）这一拍不判。
/// </summary>
[Component(description: "DI 值监控组件")]
public class DiSensorComponent : ComponentBase
{
    #region SC 

    [SCEditor("-1", "IO", "监控的 DI 索引", Required = true)]
    public int DiIndex { get; set; } = -1;

    [SCEditor("High", "Config", "触发有效电平：High=高电平报警，Low=低电平报警")]
    public TriggerLevel TriggerLevel { get; set; } = TriggerLevel.High;

    [SCEditor("True", "Config", "是否直接报警（false = 只提供触发状态，由宿主处理）")]
    public bool AlarmEnabled { get; set; } = true;

    #endregion

    #region EC 

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "10000", "200", "报警防抖：处于报警电平持续满该时长才算触发、才报警")]
    public int DebounceMs
    {
        get { return GetEcInt(nameof(DebounceMs)); }
        set { SetEcInt(nameof(DebounceMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("开关信号异常", AlarmCategory.SensorError,
        Description = "DI 数字输入处于配置的报警电平",
        Solution = "检查对应设备/互锁状态及该 DI 点接线")]
    public string SensorAlarm = nameof(SensorAlarm);

    #endregion

    #region 状态

    /// <summary>
    /// 是否触发（防抖后）：处于报警电平并持续满 DebounceMs；离开报警电平立刻算没触发。
    /// AlarmEnabled=False 时不报警，宿主看它自己处理。
    /// </summary>
    public bool IsTriggered { get; private set; }

    #endregion

    #region 扫描

    /// <summary>
    /// 每个扫描周期读一次 DI，按防抖判触发、报警；没配点（-1）或读不到时不判，状态与防抖计时都不动。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        if (DiIndex < 0)
        {
            return;
        }

        var level = ReadDi();
        if (level is null)
        {
            return;
        }

        bool active = level.Value == (TriggerLevel == TriggerLevel.High);
        IsTriggered = CheckAlarm(SensorAlarm, active, DebounceMs, raise: AlarmEnabled);
    }

    /// <summary>
    /// 读 DI 电平（true = 高电平）；PLC 没连或点表里没这个索引返回 null。探针测试重写它直接摆读数。
    /// </summary>
    protected virtual bool? ReadDi()
    {
        var io = IoComponent.Current;
        if (io is null || !io.TryReadDi(DiIndex, out bool on))
        {
            return null;
        }

        return on;
    }

    #endregion
}
