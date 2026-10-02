namespace xyz.Components.Wafers;

/// <summary>
/// 流水里的变动类型。
/// </summary>
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
}
