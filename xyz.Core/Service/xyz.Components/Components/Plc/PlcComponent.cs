using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// PLC 组件的公共底座：只管连接、断线重连、掉线报警；读什么数据、对外给哪个接口（IPlcIo / IPlcBlock）由品牌子类自己定
/// </summary>
public abstract class PlcComponent : ComponentBase
{
    protected PlcComponent()
    {
        // 开机准备排最前：IO 点表、轴的数据块都要在它后面
        InitOrder = 100;
    }

    /// <summary>
    /// 开机准备：连 PLC（连不上后台照样重连）
    /// </summary>
    public override bool InitComponent()
    {
        bool opened = Open();
        if (!opened)
        {
            LogHelper.Error(Name, "PLC 连接失败");
        }

        return base.InitComponent() && opened;
    }

    #region Machine

    [Machine("", "Plc", "PLC 地址（IP 或倍福 AmsNetId；倍福填 Local = 连本机运行时/仿真器），空 = 接线未定，不连也不报警")]
    public string Host { get; set; } = string.Empty;

    [Machine("851", "Plc", "端口（倍福 ADS 一般 851，OPC UA 一般 4840）")]
    public int Port { get; set; } = 851;

    #endregion

    #region  EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "500", "60000", "3000", "断线重连间隔")]
    public int ReconnectIntervalMs
    {
        get { return GetEcInt(nameof(ReconnectIntervalMs)); }
        set { SetEcInt(nameof(ReconnectIntervalMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, null, "0", "1000", "10", "通讯断开后最多重连几次，还连不上就停下，人工复位后重新开始连；0 = 不自动重连")]
    public int ReconnectMaxCount
    {
        get { return GetEcInt(nameof(ReconnectMaxCount)); }
        set { SetEcInt(nameof(ReconnectMaxCount), value); }
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
    /// 通讯是否连着（SV）。断了以后读接口一律失败；连接换代时通知子类清掉自己的缓存——
    /// 让上层知道"读不到"，而不是拿陈旧值当真。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "PLC 通讯是否连接")]
    public bool IsConnected
    {
        get => Volatile.Read(ref _connected) != 0;
        protected set
        {
            int next = value ? 1 : 0;
            if (Interlocked.Exchange(ref _connected, next) == next)
            {
                return;
            }

            OnConnectionChanged(value);
        }
    }

    private int _connected;
    private volatile bool _closed;
    private readonly object _scanGate = new();

    #endregion

    #region Alarm

    [Alarm("PLC 通讯断开", AlarmCategory.IrrecoverableError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "与 PLC 的连接断开或建立失败，所有 IO 读写停摆",
        Solution = "检查网线、PLC 上电与地址配置（machine.xml 的 Host/Port）；恢复后复位清警（重连次数用完停下来的，复位会重新开始连）")]
    public string PlcOfflineAlarm = nameof(PlcOfflineAlarm);

    #endregion

    #region 连接

    /// <summary>
    /// 断线重连：跟 LoadPort、RFID、机械手驱动同一套——后台重连、最多 EC ReconnectMaxCount 次、人工复位从头再来。
    /// </summary>
    private readonly DriverReconnector _reconnector = new();

    public bool Open()
    {
        _closed = false;
        if (string.IsNullOrWhiteSpace(Host))
        {
            LogHelper.Info($"[{FullPath}] 未配 Host，PLC 空转（装机没接 PLC）");
            return true;
        }

        _reconnector.Enable();
        IsConnected = ConnectDevice();
        return IsConnected;
    }

    /// <summary>
    /// 断开连接；与 Open 成对，宿主退出时调用。
    /// </summary>
    public void Close()
    {
        _reconnector.Disable();
        lock (_scanGate)
        {
            _closed = true;
            IsConnected = false;
            DisconnectDevice();
        }
    }

    /// <summary>
    /// 人工复位：重连次数清零、马上重新开始连（停下来的也接着连），再清本组件的报警。
    /// </summary>
    public override object? Reset()
    {
        _reconnector.Rearm();
        return base.Reset();
    }

    /// <summary>
    /// 连设备；品牌子类实现（倍福走 ADS、OPC UA 建会话……），连上返回 true。
    /// </summary>
    protected abstract bool ConnectDevice();

    /// <summary>
    /// 断开设备；品牌子类实现。
    /// </summary>
    protected abstract void DisconnectDevice();

    /// <summary>
    /// 连接状态变了（连上或断开）；子类在这里清掉上一条连接读回来的缓存。
    /// </summary>
    protected virtual void OnConnectionChanged(bool connected)
    {
    }

    #endregion

    #region 扫描

    /// <summary>
    /// 扫描周期：断了就按间隔在后台重连（满 EC ReconnectMaxCount 次停下，掉线报警一直在），连着就让子类读数据。
    /// 本组件自己就是一棵扫描树的根，由装配显式 Start——它不是模块，不在模块列表里。
    /// </summary>
    protected override void OnScan()
    {
        lock (_scanGate)
        {
            ScanPlc();
        }
    }

    private void ScanPlc()
    {
        base.OnScan();

        if (_closed || string.IsNullOrWhiteSpace(Host))
        {
            return;
        }

        // 停下来不另报警：下面的掉线报警断着就一直在，人看到了复位就会重新开始连。
        _reconnector.Check(FullPath, IsConnected, ReconnectIntervalMs, ReconnectMaxCount, () =>
        {
            IsConnected = ConnectDevice();
        });
        if (IsConnected)
        {
            RefreshData();
        }

        CheckAlarm(PlcOfflineAlarm, !IsConnected, OfflineDebounceMs);
    }

    /// <summary>
    /// 每拍连着的时候调：子类把自己要的数据读回缓存，读失败自己报警
    /// </summary>
    protected abstract void RefreshData();

    #endregion

    /// <summary>
    /// 宿主退出：断开 PLC
    /// </summary>
    public override void OnStopping()
    {
        Close();
    }
}
