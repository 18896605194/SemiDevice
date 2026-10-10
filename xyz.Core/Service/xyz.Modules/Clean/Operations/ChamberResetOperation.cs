using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 腔体复位（平台默认做法）：清错指令在这之前已经发了——腔体 Reset 先走组件基类的复位，一路递归到每根轴，轴自己发驱动器复位；
/// 这里等所有轴把复位做完。气缸、喷嘴的复位只是清报警，不用等。
/// </summary>
internal sealed class ChamberResetOperation : ChamberStepOperation
{
    private readonly BaseChamberModule _chamber;
    private bool _started;

    public ChamberResetOperation(BaseChamberModule chamber, int timeout)
        : base($"{chamber.Name} Reset", chamber.Name, timeout)
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
        foreach (var axis in _chamber.Axes)
        {
            Await(axis, ChamberDeviceAction.Reset);
        }
    }
}
