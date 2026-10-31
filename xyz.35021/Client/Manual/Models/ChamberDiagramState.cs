namespace xyz._35021.Client.Manual.Models;

/// <summary>由 ViewModel 发布的一次设备显示数据；各字段直接绑定控件的依赖属性。</summary>
public sealed record ChamberDiagramState
{
    public double Angle { get; init; }
    public bool IsRaised { get; init; }
    public bool IsUnknown { get; init; }
    public bool IsMoving { get; init; }
    public bool IsError { get; init; }
    public bool IsFlowing { get; init; }
    public double RotationSpeed { get; init; }
    public bool RotateClockwise { get; init; } = true;
}
