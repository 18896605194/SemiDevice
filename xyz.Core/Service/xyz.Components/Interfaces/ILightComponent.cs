namespace xyz.Components.Interfaces;

/// <summary>
/// 四色灯（带蜂鸣器） 接口
/// </summary>
public interface ILightComponent
{
    /// <summary>红灯是否亮（最后一次成功输出的状态，下同）。</summary>
    bool IsRedOn { get; }

    bool IsYellowOn { get; }

    bool IsGreenOn { get; }

    bool IsBlueOn { get; }

    bool IsBuzzerOn { get; }

    void SetRed(bool isOn);

    void SetYellow(bool isOn);

    void SetGreen(bool isOn);

    void SetBlue(bool isOn);

    void SetBuzzer(bool isOn);
}
