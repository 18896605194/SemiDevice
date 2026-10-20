namespace xyz.Components.Interfaces;

/// <summary>
/// 心跳：按时间轮流给一个 DO 写通、断，PLC 看它判上位机在不在
/// </summary>
public interface IHeartbeat : IDeviceComponent
{
    bool IsAlive { get; }
}
