using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 对准器自己的能力；跟机械手交互走站点基类的 ITransferStation
/// </summary>
public interface IAligner : IModule
{
    /// <summary>
    /// 回零：台上有片的接着对一遍；状态 → 互锁查过才挂上，被拒带原因返回
    /// </summary>
    HandleResult<ModuleRoutine> Home();

    /// <summary>
    /// 复位被拒的原因（复位本身是 IModule.Reset）
    /// </summary>
    HandleResult? CheckReset();

    /// <summary>
    /// 中止被拒的原因（中止本身是 IModule.Abort）
    /// </summary>
    HandleResult? CheckAbort();

    /// <summary>
    /// 台上那片对好了（放上来以后对准做成了，之后没转过台面）；台上没片为 false
    /// </summary>
    bool IsWaferAligned { get; }
}
