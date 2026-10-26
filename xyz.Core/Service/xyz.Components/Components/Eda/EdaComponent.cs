using System.Net;
using System.Text.RegularExpressions;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Secs.Eda;
using xyz.Secs.Eda.Model;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// EDA（Interface A）：给厂里 FDC / APC 这类系统采数据的 Web 服务，machine.xml 顶层的 Eda 节点。
/// 会话、自描述、采集计划的逻辑和 Freeze 2 传输都在 xyz.Secs 的 EdaServer 里；这里给它设定（machine.xml、EC）、设备侧（组件树建自描述、读值）、
/// 存盘、日志，把组件事件（RaiseEvent）和报警交进去，跟着宿主起停。
/// 跟 EAP 互不依赖：EAP 没连、GEM 离线时照样能采；EAP 链路启用时自描述里多带 E164 的 GEM300 部分
/// </summary>
[Component(description: "EDA（Interface A）：给厂里 FDC / APC 这类系统采数据的 Web 服务（会话和权限 E132、设备自描述 E125、采集计划 E134）")]
public class EdaComponent : ComponentBase, IEda, IEventReceiver, IEdaSettings, IEdaLog
{
    /// <summary>
    /// 宿主退出时最多等多久把攒着的发完、关会话
    /// </summary>
    private const int StopWaitMs = 3000;

    private static readonly Regex NamePattern = new("^[a-zA-Z]([a-zA-Z\\-_. 0-9]*[a-zA-Z\\-_.0-9])?$");

    private EdaEquipment? _equipment;
    private EdaServer? _server;
    private IAlarm? _alarms;

    #region Machine

    [Machine("False", "Eda", "启用 EDA（Interface A）：True 开机起 Web 服务，厂里 FDC / APC 连这个口采数据")]
    public bool IsEnable { get; set; }

    [Machine("0.0.0.0", "Eda", "监听哪个地址：0.0.0.0 全部网卡（厂里系统在别的机器上），localhost 只本机，也可以写某张网卡的 IP")]
    public string Host { get; set; } = "0.0.0.0";

    [Machine("8790", "Eda", "端口（HTTP，SOAP 1.2），别跟 gRPC、HSMS 的端口撞")]
    public int Port { get; set; } = 8790;

    [Machine("Equipment", "Eda", "设备名：EDA 结构树的根、消息头的 From；字母开头，只能有字母、数字、- _ . 空格")]
    public string EquipmentName { get; set; } = "Equipment";

    [Machine("Default", "Eda", "存权限表、持久采集计划的库（machine.xml Database 下的节点名）")]
    public string Database { get; set; } = "Default";

    [Machine("10", "Eda", "最多同时几个客户端会话；客户端用 SetMaxSessions 改过以后以库里存的为准")]
    public int DefaultMaxSessions { get; set; } = 10;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "s", "0", "3600", "60", "隔多久问一次客户端还在不在（0 不问）")]
    public int SessionPingIntervalSec
    {
        get { return GetEcInt(nameof(SessionPingIntervalSec)); }
        set { SetEcInt(nameof(SessionPingIntervalSec), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "", "1", "100", "3", "连着几次问不到客户端就关它的会话")]
    public int SessionPingFailLimit
    {
        get { return GetEcInt(nameof(SessionPingFailLimit)); }
        set { SetEcInt(nameof(SessionPingFailLimit), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "500", "60000", "5000", "给客户端发一条（数据、通知、问活）最多等多久")]
    public int SendTimeoutMs
    {
        get { return GetEcInt(nameof(SendTimeoutMs)); }
        set { SetEcInt(nameof(SendTimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "10", "60000", "100", "定周期采样最短的周期，客户端要得更短的计划定义不进来")]
    public int MinTraceIntervalMs
    {
        get { return GetEcInt(nameof(MinTraceIntervalMs)); }
        set { SetEcInt(nameof(MinTraceIntervalMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "", "10", "100000", "1000", "一个客户端的发送队列最多堆几条，再多新采到的丢掉、报跟不上了")]
    public int MaxPendingReports
    {
        get { return GetEcInt(nameof(MaxPendingReports)); }
        set { SetEcInt(nameof(MaxPendingReports), value); }
    }

    #endregion

    /// <summary>
    /// 在不在监听
    /// </summary>
    public bool IsListening
    {
        get
        {
            if (_server is null)
            {
                return false;
            }

            return _server.IsListening;
        }
    }

    /// <summary>
    /// 客户端先连的接口发现地址
    /// </summary>
    public string DiscoveryAddress => BaseAddress() + EdaServer.DiscoveryPath;

    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        if (Port < 1 || Port > 65535)
        {
            throw new InvalidOperationException($"{FullPath} 的 Port={Port} 不对，要 1~65535");
        }

        if (!TryListenAddress(out _))
        {
            throw new InvalidOperationException($"{FullPath} 的 Host={Host} 不对，要 0.0.0.0、localhost 或一个 IP");
        }

        if (!NamePattern.IsMatch(EquipmentName))
        {
            throw new InvalidOperationException($"{FullPath} 的 EquipmentName={EquipmentName} 不对：字母开头，只能有字母、数字、- _ . 空格");
        }
    }

    /// <summary>
    /// 启用了就建设备自描述、建 EdaServer（读回存着的权限、计划）
    /// </summary>
    public override void Bind()
    {
        base.Bind();
        if (!IsEnable)
        {
            LogHelper.Info(this, "EDA 未启用（Eda 的 IsEnable=False）");
            return;
        }

        _equipment = new EdaEquipment(EquipmentName, this);
        _server = new EdaServer(this, _equipment, new EdaStore(Database, this), this);
    }

    /// <summary>
    /// 设备都跑起来以后开始收报警、起 Web 服务；起不来（端口被占这类）只记错误，设备照常跑
    /// </summary>
    public override void OnStarted()
    {
        base.OnStarted();
        if (_server is null)
        {
            return;
        }

        _alarms = IocHelper.GetService<IAlarm>();
        if (_alarms is not null)
        {
            _alarms.AlarmChanged += OnAlarmChanged;
        }

        TryListenAddress(out var address);
        try
        {
            _server.Start(new IPEndPoint(address, Port), BaseAddress());
            LogHelper.Info(this, $"EDA 开始监听 {Host}:{Port}，接口发现地址 {DiscoveryAddress}");
        }
        catch (Exception exception)
        {
            LogHelper.Error(this, $"EDA 起不来（端口 {Port} 被占？），厂里系统连不上，设备照常跑：{exception.Message}");
        }
    }

    /// <summary>
    /// 宿主退出：不再收报警，停计划（攒着的发出去）、关会话、停 Web 服务
    /// </summary>
    public override void OnStopping()
    {
        if (_alarms is not null)
        {
            _alarms.AlarmChanged -= OnAlarmChanged;
        }

        _server?.Stop(StopWaitMs);
        base.OnStopping();
    }

    #region 收事件、报警

    /// <summary>
    /// 组件报了事件：没有在跑的采集计划什么都不做；有的话翻成 EDA 事件交进去（在报事件的线程上，只读值、排队，不等）
    /// </summary>
    public void Report(ComponentBase source, string code, params GemData[] data)
    {
        if (_server is null || _equipment is null || !_server.IsCollecting)
        {
            return;
        }

        try
        {
            foreach (var context in _equipment.Resolve(source, code, data))
            {
                _server.Report(context);
            }
        }
        catch (Exception exception)
        {
            LogHelper.Warn(this, $"{source.FullPath} 的事件 {code} 交给采集计划时出错：{exception.Message}");
        }
    }

    private void OnAlarmChanged(AlarmItem alarm)
    {
        if (_server is null || _equipment is null || !_server.IsCollecting)
        {
            return;
        }

        string? locator = _equipment.LocatorOf(alarm.SourcePath);
        if (locator is null)
        {
            return;
        }

        var time = alarm.RaisedAt.LocalDateTime;
        if (!alarm.IsActive && alarm.ClearedAt.HasValue)
        {
            time = alarm.ClearedAt.Value.LocalDateTime;
        }

        string exceptionId = $"{alarm.SourcePath}.{alarm.AlarmCode}";
        try
        {
            _server.ReportException(locator, exceptionId, alarm.AlarmText, Severity(alarm.Level), alarm.IsActive, time);
        }
        catch (Exception exception)
        {
            LogHelper.Warn(this, $"报警 {exceptionId} 交给采集计划时出错：{exception.Message}");
        }
    }

    private static EdaSeverity Severity(AlarmLevel level)
    {
        switch (level)
        {
            case AlarmLevel.Fatal:
                return EdaSeverity.Fatal;
            case AlarmLevel.Warn:
                return EdaSeverity.Warning;
            default:
                return EdaSeverity.Error;
        }
    }

    #endregion

    #region 日志

    void IEdaLog.Info(string message)
    {
        LogHelper.Info(this, message);
    }

    void IEdaLog.Warn(string message)
    {
        LogHelper.Warn(this, message);
    }

    void IEdaLog.Error(string message)
    {
        LogHelper.Error(this, message);
    }

    #endregion

    private bool TryListenAddress(out IPAddress address)
    {
        if (string.Equals(Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            address = IPAddress.Loopback;
            return true;
        }

        return IPAddress.TryParse(Host, out address!);
    }

    /// <summary>
    /// 回给客户端的地址：监听全部网卡时用本机名（接口发现会优先按客户端连进来的地址回）
    /// </summary>
    private string BaseAddress()
    {
        string host = Host;
        if (host == "0.0.0.0")
        {
            host = Dns.GetHostName();
        }

        return $"http://{host}:{Port}";
    }
}
