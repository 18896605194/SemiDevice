namespace xyz.Components.Interfaces;


public interface IPlc
{
    bool IsConnected { get; }

    /// <summary>连接状态变化时递增；旧连接的状态和待发指令不得跨代使用。</summary>
    long ConnectionGeneration { get; }

    IDisposable SubscribeInput<T>(string path, Action<T> received) where T : unmanaged;

    /// <summary>每次连接先读取 PLC 指令并调用 initialize；写成功才调用 written。</summary>
    IDisposable SubscribeOutput<T>(string path, Func<T> desired, Action<T> initialize,
        Action<T> written) where T : unmanaged;

    /// <summary>
    /// 登记一个要每拍轮询的数据块。轴状态使用 SubscribeInput 通知订阅。
    /// 重复登记是空操作；空路径忽略（接线未定）。
    /// </summary>
    void Register(string path);

    /// <summary>
    /// 读一个 DI 点。读不到返回 false（没连上、DI 块没配、索引越界），值只在返回 true 时有意义。
    /// </summary>
    bool TryReadDi(int index, out bool on);

    /// <summary>
    /// 写一个 DO 点。
    /// </summary>
    bool WriteDo(int index, bool on);

    /// <summary>
    /// 回读一个 DO 点当前的输出状态（界面显示"这个阀现在是开还是关"用）。
    /// 读的是 PLC 上一拍回来的实际值，不是上位机写下去的期望值——写没写进去看这个。
    /// </summary>
    bool TryReadDo(int index, out bool on);

    /// <summary>
    /// 读一个 AI 点的原始码。标定（原始码 → 工程值）是 AI 组件的事，这儿只给原始数。
    /// </summary>
    bool TryReadAi(int index, out double value);

    /// <summary>
    /// 写一个 AO 点。
    /// </summary>
    bool WriteAo(int index, double value);

    /// <summary>
    /// 回读一个 AO 点当前的输出值；用途同 <see cref="TryReadDo"/>。
    /// </summary>
    bool TryReadAo(int index, out double value);

    /// <summary>
    /// 取一个数据块最近一拍的内容；没连上、块没登记、还没读到过都返回 false。
    /// </summary>
    bool TryReadBlock(string path, out byte[] block);

    /// <summary>
    /// 往一个数据块写：直接下发，不经缓存。
    /// </summary>
    bool WriteBlock(string path, byte[] data);
}
