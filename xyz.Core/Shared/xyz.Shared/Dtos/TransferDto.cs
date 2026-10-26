namespace xyz.Shared.Dtos;

/// <summary>
/// 搬完的一趟（手动传片成功时回给界面，走 JSON）：用了哪台机械手、哪只手。
/// </summary>
public class TransferDoneDto
{
    public string Robot { get; set; } = string.Empty;

    public int Arm { get; set; }

    public string Source { get; set; } = string.Empty;

    public int SourceSlot { get; set; }

    public string Target { get; set; } = string.Empty;

    public int TargetSlot { get; set; }
}
