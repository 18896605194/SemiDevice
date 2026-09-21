using xyz.Common.Log;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

[Component(description: "PLC 组件（全系统 IO 底座）")]
public class PlcComponent : ComponentBase
{
    /// <summary>
    /// 当前 PLC；sc.xml 里装出来即生效。气缸/轴/腔体都从这儿拿，冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static PlcComponent? Current { get; set; }

    public PlcComponent()
    {
        Current = this;
    }

    #region SC

    [SCEditor("", "Plc", "PLC 品牌（空 = 装机没接 PLC）")]
    public string Brand { get; set; } = string.Empty;

    [SCEditor("", "Plc", "PLC 地址（IP 或倍福 AmsNetId），空 = 接线未定，不连也不报警")]
    public string Host { get; set; } = string.Empty;

    [SCEditor("851", "Plc", "端口")]
    public int Port { get; set; } = 851;

    [SCEditor("", "Plc", "DI 数据块名：气缸/传感器按索引读位，位就从这一块里取")]
    public string DiDataPath { get; set; } = string.Empty;

    [SCEditor("", "Plc", "DO 数据块名：按索引写位写到这一块")]
    public string DoDataPath { get; set; } = string.Empty;

    [SCEditor("256", "Plc", "DI/DO 点数（每块多少位，用于越界校验）")]
    public int IoPointCount { get; set; } = 256;

    #endregion

    #region  EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "500", "60000", "3000", "断线重连间隔")]
    public int ReconnectIntervalMs
    {
        get { return GetEcInt(nameof(ReconnectIntervalMs)); }
        set { SetEcInt(nameof(ReconnectIntervalMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "30000", "1000", "掉线报警防抖（网络抖一下不立刻报）")]
    public int OfflineDebounceMs
    {
        get { return GetEcInt(nameof(OfflineDebounceMs)); }
        set { SetEcInt(nameof(OfflineDebounceMs), value); }
    }

    #endregion

    #region SV

    /// <summary>
    /// 通讯是否连着（SV）。断了以后缓存还是上一拍的值，所以读接口一律失败——
    /// 让上层知道"读不到"，而不是拿陈旧值当真。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "PLC 通讯是否连接")]
    public bool IsConnected { get; protected set; }

    #endregion

    #region Alarm

    [Alarm("PLC 通讯断开", AlarmCategory.CommunicationError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "与 PLC 的连接断开或建立失败，所有 IO 读写停摆",
        Solution = "检查网线、PLC 上电与地址配置（sc.xml 的 Host/Port）；恢复后复位清警")]
    public string PlcOfflineAlarm = nameof(PlcOfflineAlarm);

    [Alarm("PLC 数据块读写失败", AlarmCategory.CommunicationError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "连接正常但某个数据块读或写失败，通常是块名配错或 PLC 侧未定义",
        Solution = "核对数据块名（组件的 PlcDataPath / sc.xml 的 DiDataPath、DoDataPath）与 PLC 程序里的符号")]
    public string PlcDataErrorAlarm = nameof(PlcDataErrorAlarm);

    #endregion

    #region 连接

    private int _reconnectCountdownMs;

    /// <summary>
    /// 连 PLC；由装配在模块启动之前调用。Host 空 = 没接，直接算成功并空转。
    /// </summary>
    public bool Open()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            LogHelper.Info($"[{FullPath}] 未配 Host，PLC 空转（装机没接 PLC）");
            return true;
        }

        IsConnected = ConnectDevice();
        return IsConnected;
    }

    /// <summary>
    /// 断开连接；与 Open 成对，宿主退出时调用。
    /// </summary>
    public void Close()
    {
        DisconnectDevice();
        IsConnected = false;
    }

    /// <summary>
    /// 连设备；品牌子类实现（倍福走 ADS、西门子走 S7……）。默认没有实现，视为连不上。
    /// </summary>
    protected virtual bool ConnectDevice()
    {
        LogHelper.Warn($"[{FullPath}] {GetType().Name} 没有实现 ConnectDevice，PLC 连不上");
        return false;
    }

    /// <summary>
    /// 断开；品牌子类实现。默认什么都不做。
    /// </summary>
    protected virtual void DisconnectDevice()
    {
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
    /// 读一块；品牌子类实现。默认没有实现。
    /// </summary>
    protected virtual bool ReadDevice(string path, out byte[] data)
    {
        data = [];
        return false;
    }

    /// <summary>
    /// 写一块；品牌子类实现。默认没有实现。
    /// </summary>
    protected virtual bool WriteDevice(string path, byte[] data)
    {
        return false;
    }

    #endregion

    #region DI / DO 按索引读写（气缸、传感器用这一套）

    /// <summary>
    /// 读一个 DI 点。读不到返回 false（没连上、DI 块没配、索引越界），值只在返回 true 时有意义。
    /// 点号从 0 起，块内低位在前——实际位序装机时跟电控确认。
    /// </summary>
    public bool TryReadDi(int index, out bool on)
    {
        on = false;
        if (!TryGetBit(DiDataPath, index, out on))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 写一个 DO 点：按缓存里的 DO 块改这一位再整块下发。
    /// DO 块只有本组件写，所以以缓存为准不会跟别人打架；
    /// 品牌若支持按位直写，子类重写这个方法更省一次整块下发。
    /// </summary>
    public virtual bool WriteDo(int index, bool on)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(DoDataPath) || !IsValidIoIndex(index))
        {
            return false;
        }

        byte[] updated;
        lock (_cacheGate)
        {
            if (!_cache.TryGetValue(DoDataPath, out var cached))
            {
                return false;
            }

            // 换新数组再改：别人可能正拿着旧引用读这一拍。
            updated = (byte[])cached.Clone();
            int offset = index / 8;
            if (offset >= updated.Length)
            {
                return false;
            }

            byte mask = (byte)(1 << (index % 8));
            updated[offset] = on ? (byte)(updated[offset] | mask) : (byte)(updated[offset] & ~mask);
            _cache[DoDataPath] = updated;
        }

        return WriteDevice(DoDataPath, updated);
    }

    private bool TryGetBit(string path, int index, out bool on)
    {
        on = false;
        if (!IsValidIoIndex(index) || !TryReadBlock(path, out var block))
        {
            return false;
        }

        int offset = index / 8;
        if (offset >= block.Length)
        {
            return false;
        }

        on = (block[offset] & (1 << (index % 8))) != 0;
        return true;
    }

    private bool IsValidIoIndex(int index)
    {
        return index >= 0 && index < IoPointCount;
    }

    #endregion

    #region 扫描

    /// <summary>
    /// 扫描周期：连着就把登记的块整块读回缓存，断了就按间隔重连。
    /// 本组件自己就是一棵扫描树的根，由装配显式 Start——它不是模块，不在模块列表里。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();

        if (string.IsNullOrWhiteSpace(Host))
        {
            return;
        }

        if (!IsConnected)
        {
            Reconnect();
        }
        else
        {
            RefreshCache();
        }

        CheckAlarm(PlcOfflineAlarm, !IsConnected, OfflineDebounceMs);
    }

    private void Reconnect()
    {
        // ScanLoop 固定 50ms 一拍，按间隔倒计时，别每拍都去敲一次连不上的设备。
        _reconnectCountdownMs -= 50;
        if (_reconnectCountdownMs > 0)
        {
            return;
        }

        _reconnectCountdownMs = ReconnectIntervalMs;
        IsConnected = ConnectDevice();
        if (IsConnected)
        {
            LogHelper.Info($"[{FullPath}] PLC 重连上了");
        }
    }

    private void RefreshCache()
    {
        string[] paths;
        lock (_cacheGate)
        {
            paths = [.. _paths];
        }

        // DI/DO 块也要跟着读：DI 给气缸判到位，DO 读回来才能做按位改写。
        foreach (var path in Enumerate(paths))
        {
            if (!ReadDevice(path, out var data))
            {
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
        if (!string.IsNullOrWhiteSpace(DiDataPath))
        {
            yield return DiDataPath;
        }

        if (!string.IsNullOrWhiteSpace(DoDataPath))
        {
            yield return DoDataPath;
        }

        foreach (var path in registered)
        {
            yield return path;
        }
    }

    #endregion
}
