using xyz.Modules;

namespace xyz._35021.Module.Clean.Operation;

/// <summary>
/// 计时操作：按给定时长走完就算成功，不发任何指令。
/// 腔体驱动还没定，先用它把装配、站点交互环、搬运这条链路跑通。
/// 驱动接上之后按动作逐个换掉（照 Robot 的 HomeOperation 写：发指令 → 等回复 → 超时判负），
/// 换完这个类就可以删了。
/// </summary>
public sealed class TimedOperation : ModuleOperation
{
    private readonly int _milliseconds;

    public TimedOperation(string name, int milliseconds) : base(name)
    {
        _milliseconds = milliseconds;
    }

    protected override void OnScan()
    {
        if (Watch.ElapsedMilliseconds >= _milliseconds)
        {
            Complete();
        }
    }
}
