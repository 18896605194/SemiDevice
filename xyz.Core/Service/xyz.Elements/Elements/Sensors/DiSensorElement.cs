using System.Diagnostics;
using xyz.Elements;
using xyz.Elements.Attributes;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Tools;

namespace xyz.Elements.Elements;

/// <summary>
/// DI 值监控元件：单个 DI，电平触发 + 报警防抖（EC DebounceMs）。别的元件、模块用它走 <see cref="IDiSensor"/>。
/// </summary>
[Element(description: "DI 值监控元件")]
public class DiSensorElement : ElementBase, IDiSensor
{
    #region Machine

    [Machine("-1", "IO", "监控的 DI 索引", Required = true)]
    public int DiIndex { get; set; } = -1;

    [Machine("true", "Config", "触发有效电平：true=高电平触发，false=低电平触发")]
    public bool TriggerValue { get; set; } = true;

    [Machine("True", "Config", "是否直接报警（false = 只提供触发状态，由宿主处理）")]
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

    [Alarm("开关信号异常", AlarmCategory.EquipmentStatusWarning,
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

    private volatile int _level = -1;

    /// <summary>
    /// 最近一次读到的原始电平（true=高电平、false=低电平）；还没读到、读失败或没配点时为 null。
    /// 安全联锁按它判断 DI 条件，保证跟这个元件自身的读取路径一致，不再直接摸一遍 PLC。
    /// </summary>
    public bool? CurrentLevel => _level < 0 ? null : _level != 0;

    /// <summary>两边都防抖后的触发状态：-1 = 读不到或还没稳下来，0 = 没触发，1 = 触发。</summary>
    private volatile int _active = -1;

    /// <summary>最近读到的触不触发（还没认下来的那个），和它从哪一拍开始一直是这样；只在扫描线程用。</summary>
    private bool? _candidate;

    private long _candidateSince;

    /// <summary>
    /// 给用的地方看的触发状态（<see cref="IDiSensor"/>）：触发、不触发都要持续满 DebounceMs 才认，抖一下不认，
    /// 没认下来之前保持原来的；读不到（没配点、PLC 没连上）为 null，重新读到要重新稳满一次。
    /// 跟报警用的 <see cref="IsTriggered"/> 不一样——那个离开报警电平立刻算没触发（恢复不用等），这里像在位传感器，"没片"也不能抖一下就当真。
    /// </summary>
    public bool? IsActive => _active < 0 ? null : _active != 0;

    /// <summary>
    /// 配了 DI 点没有（DiIndex 配成 -1 就是没配）
    /// </summary>
    public bool IsConfigured => DiIndex >= 0;

    #endregion

    /// <summary>
    /// IO 元件（开机 Bind 时取）；machine.xml 没配 Io 节点是 null
    /// </summary>
    private IIo? _io;

    public override void Bind()
    {
        _io = IocHelper.GetService<IIo>();
    }

    #region 扫描

    /// <summary>
    /// 每个扫描周期读一次 DI，按防抖判触发、报警；没配点（-1）或读不到时不判，状态与防抖计时都不动。
    /// </summary>
    protected override void OnScan()
    {
        if (DiIndex < 0)
        {
            _level = -1;
            ResetActive();
            return;
        }

        var level = ReadDi();
        _level = level is null ? -1 : level.Value ? 1 : 0;
        if (level is null)
        {
            ResetActive();
            return;
        }

        bool active = level.Value == TriggerValue;
        IsTriggered = CheckAlarm(SensorAlarm, active, DebounceMs, raise: AlarmEnabled);
        UpdateActive(active);
    }

    /// <summary>
    /// 两边都防抖：这一拍读到的跟上一拍不一样就从这一拍重新计时，一直保持满 DebounceMs 才认成 <see cref="IsActive"/>。
    /// </summary>
    private void UpdateActive(bool active)
    {
        if (_candidate != active)
        {
            _candidate = active;
            _candidateSince = Stopwatch.GetTimestamp();
        }

        if (Stopwatch.GetElapsedTime(_candidateSince).TotalMilliseconds >= DebounceMs)
        {
            _active = active ? 1 : 0;
        }
    }

    /// <summary>读不到了：认下来的状态作废，重新读到要重新稳满一次。</summary>
    private void ResetActive()
    {
        _candidate = null;
        _active = -1;
    }

    /// <summary>
    /// 读 DI 电平（true = 高电平）；PLC 没连或点表里没这个索引返回 null。探针测试重写它直接摆读数。
    /// </summary>
    protected virtual bool? ReadDi()
    {
        if (_io is null || !_io.TryReadDi(DiIndex, out bool on))
        {
            return null;
        }

        return on;
    }

    #endregion
}