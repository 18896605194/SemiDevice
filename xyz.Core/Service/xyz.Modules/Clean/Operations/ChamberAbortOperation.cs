using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 腔体中止（急停，平台默认做法）：轴的停止在这之前已经发了——腔体 Abort 先走组件基类的中止，一路递归到每根轴，轴自己发停止；
/// 气缸、阀的中止只是不再等到位、输出保持原样，所以这里把喷嘴全部停液（别中止了还在喷），再等所有轴停下。
/// </summary>
internal sealed class ChamberAbortOperation : ChamberStepOperation
{
    private readonly BaseChamberModule _chamber;
    private bool _started;

    public ChamberAbortOperation(BaseChamberModule chamber, int timeout)
        : base($"{chamber.Name} Abort", chamber.Name, timeout)
    {
        _chamber = chamber;
    }

    protected override void Advance()
    {
        if (_started)
        {
            Complete();
            return;
        }

        _started = true;
        foreach (var nozzle in _chamber.Nozzles)
        {
            if (!Send(nozzle, ChamberDeviceAction.ValveOff, nozzle.Stop))
            {
                return;
            }
        }

        foreach (var axis in _chamber.Axes)
        {
            Await(axis, ChamberDeviceAction.Stop);
        }
    }
}
