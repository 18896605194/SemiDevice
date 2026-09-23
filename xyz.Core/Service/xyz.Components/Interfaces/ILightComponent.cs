namespace xyz.Components.Interfaces;

/// <summary>
/// 四色灯（带蜂鸣器）契约：亮灯的一方只认这个接口，不依赖具体的灯组件（IO 版、PLC 版……）。
/// 亮哪个灯由上层按设备状态决定（红 = 报警，黄 = 警告，绿 = 运行），灯组件只管输出。
/// </summary>
public interface ILightComponent
{
    /// <summary>红灯是否亮（最后一次成功输出的状态，下同）。</summary>
    bool IsRedOn { get; }

    bool IsYellowOn { get; }

    bool IsGreenOn { get; }

    bool IsBlueOn { get; }

    bool IsBuzzerOn { get; }

    /// <summary>设置红灯开关；输出失败抛异常，不能把没执行的输出当成功。</summary>
    void SetRed(bool isOn);

    void SetYellow(bool isOn);

    void SetGreen(bool isOn);

    void SetBlue(bool isOn);

    void SetBuzzer(bool isOn);
}
