namespace xyz.Components.Components;

/// <summary>
/// GEM 要掉电保持的设定（E30 要求存在非易失存储里）：Host 定义的报告、事件和报告的链接、关掉的事件和报警、
/// 哪些报文断了通讯要缓存，以及缓存本身的状态。整份存一行 JSON（gem_config）。
/// 只在 E30 组件里改，改的时候拿着它的锁。
/// </summary>
internal sealed class GemConfig
{
    /// <summary>报告：RPTID → 带的变量（VID：SVID、ECID、DVID 都行），按先后。S2F33 定义。</summary>
    public Dictionary<uint, List<uint>> Reports { get; set; } = new();

    /// <summary>事件挂的报告：CEID → RPTID，按先后。S2F35 链接。</summary>
    public Dictionary<uint, List<uint>> Links { get; set; } = new();

    /// <summary>
    /// 关掉的事件（S2F37）。记关掉的不记开着的：以后新加的事件默认就是开的。
    /// </summary>
    public List<uint> DisabledEvents { get; set; } = [];

    /// <summary>关掉的报警（S5F3），同样记关掉的。</summary>
    public List<uint> DisabledAlarms { get; set; } = [];

    /// <summary>
    /// 断了通讯要缓存的报文（S2F43）：Stream → Function，Function 列表空表示这个 Stream 的都缓存。
    /// 默认空：Host 没设之前什么都不缓存（不知道缓存这回事的 Host 不会被"重连后收不到事件"坑到）。
    /// </summary>
    public Dictionary<byte, List<byte>> SpoolStreams { get; set; } = new();

    /// <summary>缓存开着（断过通讯、还没被 Host 取走或清掉）。重启也接着开着。</summary>
    public bool SpoolActive { get; set; }

    /// <summary>这一轮缓存开始的时刻。</summary>
    public DateTime? SpoolStartTime { get; set; }

    /// <summary>这一轮缓存满了的时刻（没满过为空）。</summary>
    public DateTime? SpoolFullTime { get; set; }

    /// <summary>这一轮缓存一共进来过几条（含满了被挤掉、丢掉的）。</summary>
    public uint SpoolCountTotal { get; set; }
}
