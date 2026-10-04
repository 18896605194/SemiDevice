namespace xyz.Shared.Dtos;

/// <summary>
/// 机械手站点是哪一类模块：调度图按它选卡片（LoadPort 画花篮卡片，腔体和其他站点画单片卡片），不按站点在哪个方向猜。
/// </summary>
public enum StationKind
{
    /// <summary>
    /// 其他能放片的站点（缓存、对中台……），或搬运模块表还没绑好、认不出来的站点。
    /// </summary>
    Other = 0,

    /// <summary>
    /// LoadPort。
    /// </summary>
    LoadPort = 1,

    /// <summary>
    /// 腔体。
    /// </summary>
    Chamber = 2,
}
