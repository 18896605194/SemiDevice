using xyz.Drivers.Loadport;

namespace xyz.Components.Interfaces;

/// <summary>
/// LoadPort 驱动组件（品牌指令由具体驱动出）
/// </summary>
public interface ILoadPortDriverComponent
{
    /// <summary>
    /// 连上了没有
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 设备主动报上来的事
    /// </summary>
    event Action<LoadPortDeviceEvent>? DeviceEvent;

    /// <summary>
    /// Load：只装载开门、不扫片，被拒是 null
    /// </summary>
    LoadPortCommand? Load();

    /// <summary>
    /// Unload：关门，被拒是 null
    /// </summary>
    LoadPortCommand? Unload();

    /// <summary>
    /// Load 好时原地扫一遍槽，被拒是 null
    /// </summary>
    LoadPortCommand? Map();

    /// <summary>
    /// 取最后一次扫片的槽图，被拒是 null
    /// </summary>
    LoadPortCommand? GetMap();

    /// <summary>
    /// 回原点，被拒是 null
    /// </summary>
    LoadPortCommand? Home();

    /// <summary>
    /// 夹紧载具，被拒是 null
    /// </summary>
    LoadPortCommand? Clamp();

    /// <summary>
    /// 松开载具，被拒是 null
    /// </summary>
    LoadPortCommand? Unclamp();

    /// <summary>
    /// 停止，被拒是 null
    /// </summary>
    LoadPortCommand? Stop();

    /// <summary>
    /// 清设备报错，被拒是 null
    /// </summary>
    LoadPortCommand? ResetDrive();

    /// <summary>
    /// 查设备状态，被拒是 null
    /// </summary>
    LoadPortCommand? QueryStatus();

    /// <summary>
    /// 作废一条在途指令
    /// </summary>
    void Abandon(LoadPortCommand command, string reason);

    /// <summary>
    /// 作废全部在途指令
    /// </summary>
    void AbandonAll(string reason);

    /// <summary>
    /// 断开
    /// </summary>
    void Close();
}
