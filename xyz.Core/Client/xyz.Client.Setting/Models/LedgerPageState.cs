namespace xyz.Client.Setting.Models;

/// <summary>
/// 账单调整页现在能不能用：有账才显示两栏和记录，其他情况正中给一句提示。
/// </summary>
public enum LedgerPageState
{
    /// <summary>连上了，账还没拉回来。</summary>
    Loading,

    /// <summary>有账，可以调。</summary>
    Ready,

    /// <summary>没连上后端，也没有拉到过账。</summary>
    Offline,

    /// <summary>晶圆账没开（sc.xml 没配 WaferManager，或 IsEnable=False）。</summary>
    Disabled,

    /// <summary>账开着，但没有能放片的位置（机械手的 Stations 都没配，或站点没登记槽位）。</summary>
    NoLocations,
}
