namespace xyz.Components.Enums;


public enum WaferHistoryAction
{
    /// <summary>建片。</summary>
    Created = 0,

    /// <summary>移片。</summary>
    Moved = 1,

    /// <summary>改片信息（片号、批次、载具、工艺状态）。</summary>
    Updated = 2,

    /// <summary>删片。</summary>
    Deleted = 3,

    /// <summary>开机从存盘恢复（重启前就在这个位置上的片）。</summary>
    Restored = 4,
}
