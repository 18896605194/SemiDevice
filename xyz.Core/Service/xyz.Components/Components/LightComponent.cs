using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 四色灯和蜂鸣器组件，提供各路独立开关；DO 点位由上层通过 SC 配置。
/// </summary>
[Component(description: "四色灯和蜂鸣器组件")]
public class LightComponent : ComponentBase
{
    #region SC 装机常量

    [SCEditor("-1", "IO", "红灯 DO 索引", Required = true)]
    public int DoRedIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "黄灯 DO 索引", Required = true)]
    public int DoYellowIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "绿灯 DO 索引", Required = true)]
    public int DoGreenIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "蓝灯 DO 索引", Required = true)]
    public int DoBlueIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "蜂鸣器 DO 索引", Required = true)]
    public int DoBuzzerIndex { get; set; } = -1;

    #endregion

    #region Control

    /// <summary>设置红灯开关。</summary>
    public virtual void SetRed(bool isOn)
    {
        SetOutput(DoRedIndex, isOn, nameof(DoRedIndex));
    }

    /// <summary>设置黄灯开关。</summary>
    public virtual void SetYellow(bool isOn)
    {
        SetOutput(DoYellowIndex, isOn, nameof(DoYellowIndex));
    }

    /// <summary>设置绿灯开关。</summary>
    public virtual void SetGreen(bool isOn)
    {
        SetOutput(DoGreenIndex, isOn, nameof(DoGreenIndex));
    }

    /// <summary>设置蓝灯开关。</summary>
    public virtual void SetBlue(bool isOn)
    {
        SetOutput(DoBlueIndex, isOn, nameof(DoBlueIndex));
    }

    /// <summary>设置蜂鸣器开关。</summary>
    public virtual void SetBuzzer(bool isOn)
    {
        SetOutput(DoBuzzerIndex, isOn, nameof(DoBuzzerIndex));
    }

    private void SetOutput(int doIndex, bool isOn, string configName)
    {
        if (doIndex < 0)
        {
            throw new InvalidOperationException($"{GetType().Name}.{configName} 尚未配置有效的 DO 索引。");
        }

        WriteOutput(doIndex, isOn);
    }

    /// <summary>
    /// 对接实际 IO 驱动。isOn 表示逻辑开关，具体输出电平由实现按硬件接线处理。
    /// 写入失败应抛出异常，不能将未执行的输出当作成功。
    /// </summary>
    protected virtual void WriteOutput(int doIndex, bool isOn)
    {
        throw new NotSupportedException($"{GetType().Name} 尚未实现 DO 输出控制。");
    }

    #endregion
}
