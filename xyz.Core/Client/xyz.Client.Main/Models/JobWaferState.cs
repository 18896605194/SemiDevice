namespace xyz.Client.Main.Models;

/// <summary>
/// 槽位表"状态"列显示什么（界面按它换字、换色，见 JobWaferStateDataGridTextStyle）：
/// 片的物理状态不正常（交叉、叠片、不明）时显示物理状态，否则显示工艺状态；空槽是 None，不写字。
/// </summary>
public enum JobWaferState
{
    /// <summary>空槽。</summary>
    None,

    /// <summary>还没做（待处理）。</summary>
    Idle,

    /// <summary>工艺中。</summary>
    InProcess,

    /// <summary>做完了。</summary>
    Completed,

    /// <summary>做失败了。</summary>
    Failed,

    /// <summary>中途被中止。</summary>
    Aborted,

    /// <summary>交叉片（跨槽）。</summary>
    Crossed,

    /// <summary>叠片（一个槽两片）。</summary>
    Double,

    /// <summary>有片但状态不明。</summary>
    Unknown,
}
