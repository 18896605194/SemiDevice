using xyz.Elements.Elements;
using xyz.Elements.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

public interface IAligner : IModule
{
    HandleResult<ModuleRoutine> Home();

    /// <summary>
    /// 台上那片对好了（放上来以后对准做成了，之后没转过台面）；台上没片为 false
    /// </summary>
    bool IsWaferAligned { get; }
}
