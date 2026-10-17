using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// AI 液位信号：读液位计的 AI，到了 EC 触发值就算触发s
/// </summary>
[Component(description: "AI 液位信号（读液位计的 AI，到了 EC 设的触发值算触发；跟 DI 信号一样挂在槽的 LL / L / H / HH / Overfill 上）")]
public class AiLevelComponent : ComponentBase, IDiSensor
{
    #region SC

    [SCEditor("-1", "IO", "液位计接的 AI 索引", Required = true)]
    public int AiIndex { get; set; } = -1;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Double, "L", "0", null, "0", "触发值：读数到了它就算触发；0 = 还没设，当读不到（槽的液位不知道）")]
    public double TriggerValue
    {
        get { return GetEcDouble(nameof(TriggerValue)); }
        set { SetEcDouble(nameof(TriggerValue), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "L", "0", null, "0.2", "容错：触发以后读数掉到 触发值 − 容错 以下才算没触发，液面在触发值上下晃时不来回跳")]
    public double Tolerance
    {
        get { return GetEcDouble(nameof(Tolerance)); }
        set { SetEcDouble(nameof(Tolerance), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "10000", "200", "防抖：触发、没触发都要持续满该时长才认，读数冒一下尖不认")]
    public int DebounceMs
    {
        get { return GetEcInt(nameof(DebounceMs)); }
        set { SetEcInt(nameof(DebounceMs), value); }
    }

    #endregion

    #region 状态

    /// <summary>
    /// 防抖后的触发状态：-1 = 读不到或还没稳下来，0 = 没触发，1 = 触发
    /// </summary>
    private volatile int _active = -1;

    /// <summary>
    /// 按容错比出来、还没认下来的触不触发；只在扫描线程用
    /// </summary>
    private bool? _candidate;

    private long _candidateSince;

    /// <summary>
    /// 有没有触发（容错、防抖都算好了）；读不到、触发值没设是 null
    /// </summary>
    public bool? IsActive
    {
        get
        {
            int active = _active;
            if (active < 0)
            {
                return null;
            }

            return active != 0;
        }
    }

    /// <summary>
    /// 配了 AI 点没有（AiIndex 配成 -1 就是没配）
    /// </summary>
    public bool IsConfigured => AiIndex >= 0;

    #endregion

    /// <summary>
    /// 开机提示触发值还没设：新生成的 EC 是 0，不提示现场只看到液位不知道
    /// </summary>
    public override bool InitComponent()
    {
        if (IsConfigured && TriggerValue <= 0)
        {
            LogHelper.Warn(FullPath, "EC TriggerValue（触发值）还没设，这个点当读不到，槽的液位会是不知道");
        }

        return base.InitComponent();
    }

    #region 扫描

    protected override void OnScan()
    {
        base.OnScan();
        if (AiIndex < 0)
        {
            ResetActive();
            return;
        }

        var value = ReadAi();
        double trigger = TriggerValue;
        if (value is null || trigger <= 0)
        {
            ResetActive();
            return;
        }

        // 容错：没触发时到了触发值才算到；触发着要掉到 触发值 − 容错 以下才算没到
        bool reached = value.Value >= trigger;
        if (_candidate == true)
        {
            reached = value.Value >= trigger - Tolerance;
        }

        UpdateActive(reached);
    }

    /// <summary>
    /// 两边都防抖：跟上一拍比出来的不一样就从这一拍重新计时，一直保持满 DebounceMs 才认成 <see cref="IsActive"/>
    /// </summary>
    private void UpdateActive(bool active)
    {
        if (_candidate != active)
        {
            _candidate = active;
            _candidateSince = Stopwatch.GetTimestamp();
        }

        if (Stopwatch.GetElapsedTime(_candidateSince).TotalMilliseconds < DebounceMs)
        {
            return;
        }

        if (active)
        {
            _active = 1;
        }
        else
        {
            _active = 0;
        }
    }

    /// <summary>
    /// 读不到了：认下来的状态作废，重新读到要重新稳满一次
    /// </summary>
    private void ResetActive()
    {
        _candidate = null;
        _active = -1;
    }

    /// <summary>
    /// 读液位计的 AI（点表换算后的工程值）；PLC 没连或点表里没这个索引返回 null
    /// </summary>
    protected virtual double? ReadAi()
    {
        var io = IoComponent.Current;
        if (io is null || !io.TryReadAi(AiIndex, out double value))
        {
            return null;
        }

        return value;
    }

    #endregion
}
