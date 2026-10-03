namespace xyz.Client.Setting.Models;

/// <summary>
/// 账单调整页确认框正在确认的事。
/// </summary>
public enum LedgerAction
{
    /// <summary>没在确认（确认框关着）。</summary>
    None,

    /// <summary>移账：源那片的账挪到目标空槽。</summary>
    Move,

    /// <summary>删账：源那片从账上删掉。</summary>
    Delete,

    /// <summary>补账：在目标空槽上按填的片号建一片。</summary>
    Create,
}
