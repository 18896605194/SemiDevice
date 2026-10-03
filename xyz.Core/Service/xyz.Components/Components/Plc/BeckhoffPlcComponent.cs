using TwinCAT;
using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.TypeSystem;
using xyz.Common.Log;
using xyz.Components.Attributes;

namespace xyz.Components.Components;


[Component(description: "倍福 PLC 组件（ADS）")]
public class BeckhoffPlcComponent : PlcComponent
{
    private AdsClient? _client;
    private ISymbolLoader? _symbols;
    private readonly object _deviceGate = new();

    private readonly object _handleGate = new();

    /// <summary>符号路径 → 变量句柄，块和单点均复用句柄，断开时清空。</summary>
    private readonly Dictionary<string, uint> _handles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Host 的这个取值表示连本机（按本机 AmsNetId）。</summary>
    private const string LocalHost = "Local";

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
        try
        {
            // 重连前先把上一条连接清干净，免得句柄和符号表串到新连接上。
            DisconnectDevice();

            var client = new AdsClient();

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
                LogHelper.Warn($"[{FullPath}] PLC 不在 Run 状态，暂不算连上");
                client.Dispose();
                return false;
            }

            client.AdsStateChanged += OnAdsStateChanged;
            _client = client;
            _symbols = SymbolLoaderFactory.Create(client, SymbolLoaderSettings.Default);
            return true;
        }
        catch (Exception exception)
        {
            LogHelper.Warn($"[{FullPath}] 连 PLC 失败: {exception.Message}");
            return false;
        }
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
            LogHelper.Warn($"[{FullPath}] 断开 PLC 出错: {exception.Message}");
        }
    }

    private void OnAdsStateChanged(object? sender, AdsStateChangedEventArgs e)
    {
        if (e.State.AdsState != AdsState.Run)
        {
            IsConnected = false;
        }
    }

    #endregion

    #region 块读写

    protected override bool WriteDoDevice(int index, bool on)
    {
        return WriteDevice($"{DoDataPath}[{index}]", [on ? (byte)1 : (byte)0]);
    }

    protected override bool WriteAoDevice(int index, double value)
    {
        // AO 为 REAL，与整块回读的 4 字节浮点布局一致。
        float raw = (float)value;
        return float.IsFinite(raw)
            && WriteDevice($"{AoDataPath}[{index}]", BitConverter.GetBytes(raw));
    }

    protected override bool ReadDevice(string path, out byte[] data)
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
    /// 块名 → 最近一次读失败的错误码：同样的错只记一次日志（扫描 50ms 一拍，不然一秒二十条）。
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
            LogHelper.Warn($"[{FullPath}] 读 {path} 没读成（{error}），句柄作废，下一拍重新取");
        }
    }

    protected override bool WriteDevice(string path, byte[] data)
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
    /// 块名配错这类只记日志，基类那边照常报 PlcDataErrorAlarm。
    /// </summary>
    private void OnAdsFailed(string path, Exception exception)
    {
        LogHelper.Warn($"[{FullPath}] 读写 {path} 失败: {exception.Message}");

        if (exception is AdsErrorException)
        {
            IsConnected = false;
            DisconnectDevice();
        }
    }

    #endregion
}
