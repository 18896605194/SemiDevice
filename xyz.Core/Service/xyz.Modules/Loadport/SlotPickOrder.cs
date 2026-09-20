namespace xyz.Modules;

/// <summary>
/// 从 LoadPort 取片的槽位顺序。槽位号 1 在最下面、SlotCount 在最上面。
/// </summary>
public enum SlotPickOrder
{
    /// <summary>从下往上：先取 Slot 1。</summary>
    BottomUp,

    /// <summary>从上往下：先取 Slot SlotCount。</summary>
    TopDown,
}
