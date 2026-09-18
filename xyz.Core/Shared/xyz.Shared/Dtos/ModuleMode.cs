namespace xyz.Shared.Dtos;

/// <summary>
/// 模块模式：模块是否参与自动调度。所有模块都有，Online/Offline 只改它，不动设备；
/// 手动操作不受它影响。LoadPort 的 Auto/Manual（E84 用的 Access Mode）是另一回事。
/// </summary>
public enum ModuleMode
{
    /// <summary>下线：默认初始态，不参与自动调度。</summary>
    Offline = 0,

    /// <summary>上线：参与自动调度。</summary>
    Online = 1,
}
