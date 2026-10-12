namespace xyz.Components.Interfaces;

/// <summary>运动轴的状态和动作；指令被接收后，完成看 ActionState。</summary>
public interface IAxis : IDeviceComponent
{
    /// <summary>当前反馈是否有效。</summary>
    bool HasPlcData { get; }

    /// <summary>实际位置。</summary>
    double CurrentPosition { get; }

    /// <summary>实际速度。</summary>
    double CurrentSpeed { get; }

    /// <summary>伺服是否已使能。</summary>
    bool IsServoOn { get; }

    /// <summary>是否已回零。</summary>
    bool IsHomed { get; }

    /// <summary>是否正在运动。</summary>
    bool IsBusy { get; }

    /// <summary>是否到达目标位置。</summary>
    bool IsInPosition { get; }

    /// <summary>驱动器是否报错。</summary>
    bool IsError { get; }

    /// <summary>回原点。</summary>
    bool Home();

    /// <summary>绝对定位；未给速度时使用组件自己的默认值。</summary>
    bool MoveTo(double position, double? speed = null);

    /// <summary>相对移动；未给速度时使用组件自己的默认值。</summary>
    bool MoveBy(double offset, double? speed = null);

    /// <summary>点动，速度正负表示方向。</summary>
    bool Jog(double speed);

    /// <summary>连续旋转。</summary>
    bool Spin(double speed);

    /// <summary>受控停止。</summary>
    bool Stop();

    /// <summary>急停。</summary>
    bool EmergencyStop();

    /// <summary>复位驱动器。</summary>
    bool ResetDrive();

    /// <summary>设置伺服使能。</summary>
    bool SetServo(bool enabled);
}

