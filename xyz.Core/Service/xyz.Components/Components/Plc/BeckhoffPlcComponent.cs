using TwinCAT;
using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.TypeSystem;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;


[Component(description: "倍福 PLC 组件（ADS）")]
public class BeckhoffPlcComponent : PlcComponent, IPlcIo, IPlcBlock
{
    private AdsClient? _client;
    private ISymbolLoader? _symbols;
    private readonly object _deviceGate = new();

    private readonly object _handleGate = new();

    /// <summary>符号路径 → 变量句柄，块和单点均复用句柄，断开时清空。</summary>
    private readonly Dictionary<string, uint> _handles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Host 的这个取值表示连本机（按本机 AmsNetId）。</summary>
    private const string LocalHost = "Local";

    #region Machine

    [Machine("", "Plc", "DI 块在 PLC 里的变量名（如 IO_CSharp.DigIn）：气缸/传感器的 DI 索引就是这块的数组下标")]
    public string DiDataPath { get; set; } = string.Empty;

    [Machine("", "Plc", "DO 块变量名（如 IO_CSharp.DigOut）：各部件的 DO 索引是这块的下标")]
    public string DoDataPath { get; set; } = string.Empty;

    [Machine("", "Plc", "AI 块变量名（如 IO_CSharp.AnaIn）：模拟量传感器按下标读原始码")]
    public string AiDataPath { get; set; } = string.Empty;

    [Machine("", "Plc", "AO 块变量名（如 IO_CSharp.AnaOut）：按下标写模拟量")]
    public string AoDataPath { get; set; } = string.Empty;

    [Machine("1000", "Plc", "IO 点数（每块最多多少个点，用于越界校验）")]
    public int IoPointCount { get; set; } = 1000;

    #endregion

    #region Alarm

    [Alarm("PLC 数据块读写失败", AlarmCategory.IrrecoverableError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "连接正常但某个数据块读或写失败，通常是块名配错或 PLC 侧未定义",
        Solution = "核对数据块名（组件的 PlcDataPath / machine.xml 的 DiDataPath、DoDataPath）与 PLC 程序里的符号")]
    public string PlcDataErrorAlarm = nameof(PlcDataErrorAlarm);

    #endregion

    #region 连接

    protected override bool ConnectDevice()
    {
        lock (_deviceGate)
        {
            return ConnectAds();
        }
    }

    private bool ConnectAds()
    {
        AdsClient? client = null;
        try
        {
            // 重连前先把上一条连接清干净，免得句柄和符号表串到新连接上。
            DisconnectDevice();

            client = new AdsClient();

            // Host 写 Local = 连本机的 PLC 运行时或仿真器，不用把本机 AmsNetId 写死进配置（换台机器就不对了）。
            if (string.Equals(Host, LocalHost, StringComparison.OrdinalIgnoreCase))
            {
                client.Connect(Port);
            }
            else
            {
                client.Connect(Host, Port);
            }

            if (!client.IsConnected)
            {
                client.Dispose();
                return false;
            }

            // 连上不等于能用：PLC 停着的时候读回来的是上一轮的残值。
            if (client.ReadState().AdsState != AdsState.Run)
            {
                NoteConnectFailed("PLC 不在 Run 状态，暂不算连上");
                client.Dispose();
                return false;
            }

            client.AdsStateChanged += OnAdsStateChanged;
            _symbols = SymbolLoaderFactory.Create(client, SymbolLoaderSettings.Default);
            _client = client;
            _lastConnectError = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            // 路由连上了、PLC 却不应答（仿真器没开、PLC 没起来）时 ReadState 会抛异常：这条连接也得关掉，
            // 不然它的接收线程一直挂着，每重连一次就漏两条线程。
            client?.Dispose();
            NoteConnectFailed($"连 PLC 失败: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// 上一次连不上的原因：一样的原因只记一次（断着的时候隔几秒就重连一次，不然一直刷屏），连上了清掉。
    /// </summary>
    private string _lastConnectError = string.Empty;

    private void NoteConnectFailed(string reason)
    {
        if (reason == _lastConnectError)
        {
            return;
        }

        _lastConnectError = reason;
        LogHelper.Warn(this, $"{reason}");
    }

    protected override void DisconnectDevice()
    {
        lock (_deviceGate)
        {
            DisconnectAds();
        }
    }

    private void DisconnectAds()
    {
        lock (_handleGate)
        {
            _handles.Clear();
            _readFaults.Clear();
        }

        _symbols = null;

        var client = _client;
        _client = null;
        if (client is null)
        {
            return;
        }

        try
        {
            client.AdsStateChanged -= OnAdsStateChanged;
            client.Dispose();
        }
        catch (Exception exception)
        {
            LogHelper.Warn(this, $"断开 PLC 出错: {exception.Message}");
        }
    }

    private void OnAdsStateChanged(object? sender, AdsStateChangedEventArgs e)
    {
        if (e.State.AdsState != AdsState.Run)
        {
            IsConnected = false;
        }
    }

    /// <summary>
    /// 连接换代了：缓存里是上一条连接的旧帧，清掉——读不到（false）比拿旧值当真安全。
    /// </summary>
    protected override void OnConnectionChanged(bool connected)
    {
        lock (_cacheGate)
        {
            _cache.Clear();
        }
    }

    #endregion

    #region 数据块缓存（每拍整块读回，上层从缓存取）

    private readonly object _cacheGate = new();

    /// <summary>块名 → 最近一拍读回的字节。刷新时整体换新数组，不原地改——
    /// 上层拿着旧引用取位，读到的是完整的一拍，不会半新半旧。</summary>
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>要每拍读回来的块名。</summary>
    private readonly List<string> _paths = new();

    /// <summary>
    /// 登记一个要每拍读回来的数据块（轴、腔体装配时把自己的 ReceivePlcDataPath 报进来）。
    /// 重复登记是空操作；空路径忽略（接线未定）。
    /// </summary>
    public void Register(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (_cacheGate)
        {
            if (!_paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _paths.Add(path);
            }
        }
    }

    /// <summary>
    /// 取一个数据块最近一拍的内容。没连上、块没登记、还没读到过都返回 false——
    /// 读不到和读到 0 是两回事，上层必须分开处理（比如别把读不到当成"没到位"报超时）。
    /// </summary>
    public bool TryReadBlock(string path, out byte[] block)
    {
        block = [];
        if (!IsConnected || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        lock (_cacheGate)
        {
            if (!_cache.TryGetValue(path, out var cached))
            {
                return false;
            }

            block = cached;
            return true;
        }
    }

    /// <summary>
    /// 往一个数据块写：直接下发，不经缓存。
    /// </summary>
    public bool WriteBlock(string path, byte[] data)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return WriteDevice(path, data);
    }

    /// <summary>
    /// 每拍把登记的块整块读回缓存（基类扫描在连着的时候调）
    /// </summary>
    protected override void RefreshData()
    {
        string[] paths;
        lock (_cacheGate)
        {
            paths = [.. _paths];
        }

        // DI/AI 给传感器读，DO/AO 回读 PLC 实际输出，写入请求不更新缓存。
        foreach (var path in Enumerate(paths))
        {
            if (!ReadDevice(path, out var data))
            {
                // 这一拍没读回来，缓存里那份就是旧的，拿掉：读不到就是读不到，
                // 不能让上层把上一拍（甚至 PLC 重启前）的值当成现在的值用、当成实时数据记下来。
                lock (_cacheGate)
                {
                    _cache.Remove(path);
                }

                RaiseAlarm(PlcDataErrorAlarm);
                continue;
            }

            lock (_cacheGate)
            {
                _cache[path] = data;
            }
        }
    }

    private IEnumerable<string> Enumerate(string[] registered)
    {
        foreach (var path in new[] { DiDataPath, DoDataPath, AiDataPath, AoDataPath })
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }

        foreach (var path in registered)
        {
            yield return path;
        }
    }

    #endregion

    #region DI / DO / AI / AO

    /// <summary>
    /// 读一个 DI 点。读不到返回 false（没连上、DI 块没配、索引越界），值只在返回 true 时有意义。
    /// </summary>
    public bool TryReadDi(int index, out bool on)
    {
        on = false;
        return IsValidIoIndex(index)
            && TryReadBlock(DiDataPath, out var block)
            && DecodeDi(block, index, out on);
    }

    /// <summary>
    /// 直接写一个 DO 点；不修改回读缓存，实际状态由下一次采集确认。
    /// </summary>
    public bool WriteDo(int index, bool on)
    {
        return IsConnected && IsValidIoIndex(index)
            && !string.IsNullOrWhiteSpace(DoDataPath)
            && WriteDoDevice(index, on);
    }

    /// <summary>
    /// 回读一个 DO 点当前的输出状态。DO 块每拍也读回来，所以跟读 DI 一样从缓存取；
    /// 解码共用 <see cref="DecodeDi"/>——DO 块的布局跟 DI 一块一样（都是 ARRAY OF BOOL）。
    /// </summary>
    public bool TryReadDo(int index, out bool on)
    {
        on = false;
        return IsValidIoIndex(index)
            && TryReadBlock(DoDataPath, out var block)
            && DecodeDi(block, index, out on);
    }

    /// <summary>
    /// 回读一个 AO 点当前的输出值。
    /// </summary>
    public bool TryReadAo(int index, out double value)
    {
        value = 0;
        return IsValidIoIndex(index)
            && TryReadBlock(AoDataPath, out var block)
            && DecodeAo(block, index, out value);
    }

    /// <summary>
    /// 读一个 AI 点的原始码。标定（原始码 → 工程值）是 AI 组件的事，这儿只给原始数。
    /// </summary>
    public bool TryReadAi(int index, out double value)
    {
        value = 0;
        return IsValidIoIndex(index)
            && TryReadBlock(AiDataPath, out var block)
            && DecodeAi(block, index, out value);
    }

    /// <summary>
    /// 直接写一个 AO 点；不修改回读缓存。
    /// </summary>
    public bool WriteAo(int index, double value)
    {
        return IsConnected && IsValidIoIndex(index)
            && !string.IsNullOrWhiteSpace(AoDataPath)
            && double.IsFinite(value)
            && WriteAoDevice(index, value);
    }

    /// <summary>
    /// 从 DI 块里解出第 index 点：ADS 里 ARRAY[0..n] OF BOOL 一个 BOOL 占一整字节（0/1），不是一位。
    /// </summary>
    private static bool DecodeDi(byte[] block, int index, out bool on)
    {
        on = false;
        if (index >= block.Length)
        {
            return false;
        }

        on = block[index] != 0;
        return true;
    }

    /// <summary>
    /// 从 AI 块里解出第 index 点：ARRAY[0..n] OF UINT，每点 2 字节小端原始码。
    /// </summary>
    private static bool DecodeAi(byte[] block, int index, out double value)
    {
        value = 0;
        int offset = index * sizeof(ushort);
        if (offset + sizeof(ushort) > block.Length)
        {
            return false;
        }

        value = BitConverter.ToUInt16(block, offset);
        return true;
    }

    /// <summary>
    /// 从 AO 块里解出第 index 点（回读输出）：每点 4 字节 REAL。
    /// </summary>
    private static bool DecodeAo(byte[] block, int index, out double value)
    {
        value = 0;
        int offset = index * sizeof(float);
        if (offset + sizeof(float) > block.Length)
        {
            return false;
        }

        value = BitConverter.ToSingle(block, offset);
        return true;
    }

    private bool IsValidIoIndex(int index)
    {
        return index >= 0 && index < IoPointCount;
    }

    #endregion

    #region 块读写

    private bool WriteDoDevice(int index, bool on)
    {
        return WriteDevice($"{DoDataPath}[{index}]", [on ? (byte)1 : (byte)0]);
    }

    private bool WriteAoDevice(int index, double value)
    {
        // AO 为 REAL，与整块回读的 4 字节浮点布局一致。
        float raw = (float)value;
        return float.IsFinite(raw)
            && WriteDevice($"{AoDataPath}[{index}]", BitConverter.GetBytes(raw));
    }

    /// <summary>
    /// 读一块（经 ADS 符号句柄）
    /// </summary>
    protected virtual bool ReadDevice(string path, out byte[] data)
    {
        lock (_deviceGate)
        {
            return ReadAds(path, out data);
        }
    }

    private bool ReadAds(string path, out byte[] data)
    {
        data = [];
        var client = _client;
        if (client is null || !TryGetHandle(path, out uint handle, out int size))
        {
            return false;
        }

        try
        {
            var result = client.ReadAsResult(handle, size);
            if (!result.Succeeded)
            {
                DropHandle(client, path, handle, result.ErrorCode.ToString());
                return false;
            }

            lock (_handleGate)
            {
                _readFaults.Remove(path);
            }

            data = result.Data.ToArray();
            return true;
        }
        catch (Exception exception)
        {
            OnAdsFailed(path, exception);
            return false;
        }
    }

    /// <summary>
    /// 块名 → 最近一次读失败的错误码：同样的错只记一次日志（扫描 10ms 一拍，不然一秒一百条）。
    /// </summary>
    private readonly Dictionary<string, string> _readFaults = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 没抛异常但没读成：多半是 PLC 重启、重新下载过程序，手里的句柄作废了。
    /// 把这个句柄扔掉，下一拍重新取——PLC 回来以后自己就恢复，不用重启后端。
    /// </summary>
    private void DropHandle(AdsClient client, string path, uint handle, string error)
    {
        bool isNew;
        lock (_handleGate)
        {
            _handles.Remove(path);
            isNew = !_readFaults.TryGetValue(path, out var last) || last != error;
            _readFaults[path] = error;
        }

        try
        {
            client.DeleteVariableHandle(handle);
        }
        catch
        {
            // 句柄本来就作废了，删不掉正常。
        }

        if (isNew)
        {
            LogHelper.Warn(this, $"读 {path} 没读成（{error}），句柄作废，下一拍重新取");
        }
    }

    /// <summary>
    /// 写一块或一个元素（经 ADS 符号句柄）
    /// </summary>
    protected virtual bool WriteDevice(string path, byte[] data)
    {
        lock (_deviceGate)
        {
            if (!IsConnected)
            {
                return false;
            }

            return WriteAds(path, data);
        }
    }

    private bool WriteAds(string path, byte[] data)
    {
        var client = _client;
        if (client is null)
        {
            return false;
        }

        try
        {
            // 直接取元素符号的句柄，不依赖符号加载器是否展开数组元素。
            uint handle;
            lock (_handleGate)
            {
                if (!_handles.TryGetValue(path, out handle))
                {
                    handle = client.CreateVariableHandle(path);
                    _handles[path] = handle;
                }
            }

            client.Write(handle, data);
            return true;
        }
        catch (Exception exception)
        {
            OnAdsFailed(path, exception);
            return false;
        }
    }

    private bool TryGetHandle(string path, out uint handle, out int size)
    {
        handle = 0;
        size = 0;

        var client = _client;
        var symbols = _symbols;
        if (client is null || symbols is null)
        {
            return false;
        }

        try
        {
            var symbol = symbols.Symbols[path];
            size = symbol.ByteSize;
            if (size <= 0)
            {
                return false;
            }

            lock (_handleGate)
            {
                if (!_handles.TryGetValue(path, out handle))
                {
                    handle = client.CreateVariableHandle(path);
                    _handles[path] = handle;
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            OnAdsFailed(path, exception);
            return false;
        }
    }

    /// <summary>
    /// 读写出错：ADS 层面的错误多半是连接断了，撂下标志让基类走重连；
    /// 块名配错这类只记日志，RefreshData 那边照常报 PlcDataErrorAlarm。
    /// </summary>
    private void OnAdsFailed(string path, Exception exception)
    {
        LogHelper.Warn(this, $"读写 {path} 失败: {exception.Message}");

        if (exception is AdsErrorException)
        {
            IsConnected = false;
            DisconnectDevice();
        }
    }

    #endregion
}
