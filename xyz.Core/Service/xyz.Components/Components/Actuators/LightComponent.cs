using xyz.Components.Attributes;
using xyz.Components.Interfaces;
using xyz.Tools;

namespace xyz.Components.Components;


[Component(description: "四色灯和蜂鸣器组件")]
public class LightComponent : ComponentBase, ILightComponent
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

    #region 状态（最后一次成功输出的开关）

    public bool IsRedOn { get; private set; }

    public bool IsYellowOn { get; private set; }

    public bool IsGreenOn { get; private set; }

    public bool IsBlueOn { get; private set; }

    public bool IsBuzzerOn { get; private set; }

    #endregion

    /// <summary>
    /// IO 组件（开机 Bind 时取）；sc.xml 没配 Io 节点是 null
    /// </summary>
    private IIo? _io;

    public override void Bind()
    {
        _io = IocHelper.GetService<IIo>();
    }

    #region Control

    public virtual void SetRed(bool isOn)
    {
        SetOutput(DoRedIndex, isOn, nameof(DoRedIndex));
        IsRedOn = isOn;
    }

    public virtual void SetYellow(bool isOn)
    {
        SetOutput(DoYellowIndex, isOn, nameof(DoYellowIndex));
        IsYellowOn = isOn;
    }

    public virtual void SetGreen(bool isOn)
    {
        SetOutput(DoGreenIndex, isOn, nameof(DoGreenIndex));
        IsGreenOn = isOn;
    }

    public virtual void SetBlue(bool isOn)
    {
        SetOutput(DoBlueIndex, isOn, nameof(DoBlueIndex));
        IsBlueOn = isOn;
    }


    public virtual void SetBuzzer(bool isOn)
    {
        SetOutput(DoBuzzerIndex, isOn, nameof(DoBuzzerIndex));
        IsBuzzerOn = isOn;
    }

    private void SetOutput(int doIndex, bool isOn, string configName)
    {
        if (doIndex < 0)
        {
            throw new InvalidOperationException($"{GetType().Name}.{configName} 尚未配置有效的 DO 索引。");
        }

        WriteOutput(doIndex, isOn);
    }

    protected virtual void WriteOutput(int doIndex, bool isOn)
    {
        var io = _io;
        if (io is null)
        {
            throw new InvalidOperationException("sc.xml 没配 Io 节点，四色灯没有输出通道");
        }

        if (!io.WriteDo(doIndex, isOn))
        {
            throw new InvalidOperationException($"DO{doIndex} 写入失败（PLC 没连或点表里没有这个索引）");
        }
    }

    #endregion
}
