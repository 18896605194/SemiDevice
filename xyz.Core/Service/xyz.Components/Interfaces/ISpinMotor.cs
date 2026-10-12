namespace xyz.Components.Interfaces;

/// <summary>腔体卡盘：提供旋转轴能力、取放示教位和实际旋转状态。</summary>
public interface ISpinMotor : IAxis
{
    /// <summary>取放片示教位。</summary>
    double TransferPosition { get; }

    /// <summary>卡盘实际是否在旋转。</summary>
    bool IsSpinning { get; }
}

