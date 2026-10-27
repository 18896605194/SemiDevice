using System.Globalization;
using System.Numerics;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// OPC UA PLC 组件
/// </summary>
[Component(description: "OPC UA PLC 组件")]
public class OpcUaPlcComponent : PlcComponent, IPlcIo
{
    /// <summary>
    /// 建连接要来回好几趟（取端点、建会话、激活、读命名空间表），不跟单次读写共用超时
    /// </summary>
    private const int ConnectTimeoutMs = 5000;

    private readonly object _deviceGate = new();

    /// <summary>
    /// 官方库要的日志出口：给空的，库自己的话不记，出了错由这里的 catch 用 LogHelper 记
    /// </summary>
    private readonly ITelemetryContext _telemetry = DefaultTelemetry.Create(_ => { });

    /// <summary>
    /// 客户端配置：第一次连接时连客户端证书一起建（证书放运行目录 OpcUa\pki），以后重连都用它
    /// </summary>
    private ApplicationConfiguration? _configuration;

    /// <summary>
    /// 官方库的会话，相当于倍福的 AdsClient：连上以后经它读写
    /// </summary>
    private ManagedSession? _session;

    /// <summary>
    /// 节点名 → 当前会话的 NodeId：连上以后换算一次，断开清掉
    /// </summary>
    private readonly Dictionary<string, NodeId> _nodes = new(StringComparer.Ordinal);

    /// <summary>
    /// 节点名 → 上次读失败的原因：同样的只记一次日志（不然每次读都刷一条）
    /// </summary>
    private readonly Dictionary<string, string> _faults = new(StringComparer.Ordinal);

    #region Machine

    [Machine("", "Plc", "DI 数组的节点名（如 nsu=urn:BeckhoffAutomation:Ua:PLC1;s=MAIN.DigIn，或 ns=3;s=\"IO\".\"DigIn\"）：DI 索引就是数组下标，一律从 0 数（PLC 里声明成 [1..n] 的，第 0 个是 PLC 的第 1 个）")]
    public string DiDataPath { get; set; } = string.Empty;

    [Machine("", "Plc", "DO 数组的节点名：各部件的 DO 索引是数组下标，从 0 数")]
    public string DoDataPath { get; set; } = string.Empty;

    [Machine("", "Plc", "AI 数组的节点名：模拟量传感器按下标读原始值（INT / UINT / REAL 等数值类型都认）")]
    public string AiDataPath { get; set; } = string.Empty;

    [Machine("", "Plc", "AO 数组的节点名：按下标写模拟量，按 PLC 里数组的类型写")]
    public string AoDataPath { get; set; } = string.Empty;

    [Machine("", "Plc", "登录用户名，空 = 匿名")]
    public string UserName { get; set; } = string.Empty;

    [Machine("", "Plc", "登录密码（用户名空时不用）")]
    public string Password { get; set; } = string.Empty;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "10", "10000", "50", "多久读一次 PLC（PLC 的 OPC UA 服务一般跟不上 10 ms 一次）")]
    public int ReadIntervalMs
    {
        get { return GetEcInt(nameof(ReadIntervalMs)); }
        set { SetEcInt(nameof(ReadIntervalMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "1000", "一次读写最多等多久，超时算断线、走重连")]
    public int OperationTimeoutMs
    {
        get { return GetEcInt(nameof(OperationTimeoutMs)); }
        set { SetEcInt(nameof(OperationTimeoutMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("PLC 数据读写失败", AlarmCategory.IrrecoverableError,
        AlarmLevel = AlarmLevel.Light,
        Description = "连接正常但某个节点读失败，通常是节点名配错、PLC 里没把这个变量开放给 OPC UA，或者类型不对（DI/DO 要 BOOL 数组，AI/AO 要数值数组）",
        Solution = "核对 machine.xml 的 DiDataPath、DoDataPath、AiDataPath、AoDataPath 与 PLC 里的变量；日志里有是哪个节点、什么原因")]
    public string PlcDataErrorAlarm = nameof(PlcDataErrorAlarm);

    #endregion

    #region 连接

    // 官方 SDK 只有异步接口，这里在扫描线程、重连线程、调用方线程上同步等（这些线程没有同步上下文，不会死锁），每次都带超时。
    protected override bool ConnectDevice()
    {
        lock (_deviceGate)
        {
            try
            {
                DisconnectDevice();
                OpenSession();
                ResolveNodes();
                _lastConnectError = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                // 没连成也要关掉，半截的会话会一直占着服务端的连接数。
                CloseSession();
                NoteConnectFailed($"连 PLC 失败: {exception.Message}");
                return false;
            }
        }
    }

    protected override void DisconnectDevice()
    {
        lock (_deviceGate)
        {
            _nodes.Clear();
            CloseSession();
        }

        lock (_faults)
        {
            _faults.Clear();
        }
    }

    /// <summary>
    /// 建会话：安全策略只用 None（不签名、不加密，要写明，官方默认是签名加密），登录匿名或用户名密码。
    /// 会话自带的断线重连不靠它：读写一失败就关掉会话，重连走基类（次数上限、人工复位），跟倍福一样
    /// </summary>
    private void OpenSession()
    {
        using var cancel = new CancellationTokenSource(ConnectTimeoutMs);
        _session = new ManagedSessionBuilder(GetConfiguration(), _telemetry)
            .UseEndpoint($"opc.tcp://{Host}:{Port}", MessageSecurityMode.None, SecurityPolicies.None)
            .WithUserIdentity(CreateIdentity())
            .ConnectAsync(cancel.Token)
            .GetAwaiter().GetResult();
    }

    private ApplicationConfiguration GetConfiguration()
    {
        var configuration = _configuration;
        if (configuration is null)
        {
            string certificates = System.IO.Path.Combine(AppContext.BaseDirectory, "OpcUa", "pki");
            var application = new ApplicationInstance(_telemetry)
            {
                ApplicationName = "xyz",
                ApplicationType = ApplicationType.Client,
            };
            configuration = application.Build($"urn:{Environment.MachineName}:xyz", "uri:xyz")
                .AsClient()
                .AddSecurityConfiguration(
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates("CN=xyz", CertificateStoreType.Directory, System.IO.Path.Combine(certificates, "own")),
                    certificates)
                // 只走 None：不验服务端证书，设备内网直连 PLC 用；要签名加密时这里得改成认证书
                .SetAutoAcceptUntrustedCertificates(true)
                .CreateAsync().GetAwaiter().GetResult();
            application.CheckApplicationInstanceCertificatesAsync(true).AsTask().GetAwaiter().GetResult();
            _configuration = configuration;
        }

        configuration.TransportQuotas ??= new TransportQuotas();
        configuration.TransportQuotas.OperationTimeout = OperationTimeoutMs;
        return configuration;
    }

    private IUserIdentity CreateIdentity()
    {
        if (string.IsNullOrWhiteSpace(UserName))
        {
            return new UserIdentity();
        }

        return new UserIdentity(UserName, Encoding.UTF8.GetBytes(Password));
    }

    /// <summary>
    /// 关会话：先通知服务端（最多等 1 秒），不管成没成都释放掉，免得漏连接
    /// </summary>
    private void CloseSession()
    {
        var session = _session;
        _session = null;
        if (session is null)
        {
            return;
        }

        try
        {
            session.CloseAsync(1000, true).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 连接本来就断了，通知不到服务端正常。
        }
        finally
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// 连上以后把配的节点名换成这次会话的 NodeId：认 nsu=命名空间地址;s=… 和 ns=编号;s=… 两种写法，
    /// nsu= 按服务端当前的命名空间表换编号（PLC 重新下载后编号可能变，地址不变）；认不出的记一条，读的时候照常报警
    /// </summary>
    private void ResolveNodes()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        foreach (var path in new[] { DiDataPath, DoDataPath, AiDataPath, AoDataPath })
        {
            if (string.IsNullOrWhiteSpace(path) || _nodes.ContainsKey(path))
            {
                continue;
            }

            NodeId node = NodeId.Null;
            try
            {
                node = ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(path.Trim()), session.NamespaceUris);
            }
            catch (Exception)
            {
                // 写法不对，下面按认不出处理。
            }

            if (node.IsNull)
            {
                LogHelper.Warn(this, $"节点名认不出：{path}（写法不对，或者服务端没有这个命名空间）");
                continue;
            }

            _nodes[path] = node;
        }
    }

    /// <summary>
    /// 一次往返读几个节点，结果跟传入顺序一一对应；单个读不成看各自的 StatusCode，整次没读成（断线、超时）抛异常
    /// </summary>
    private DataValue[] ReadNodes(List<NodeId> nodes)
    {
        var session = RequireSession();
        var request = new ReadValueId[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            request[i] = new ReadValueId { NodeId = nodes[i], AttributeId = Opc.Ua.Attributes.Value };
        }

        using var cancel = new CancellationTokenSource(OperationTimeoutMs);
        var response = session.ReadAsync(null, 0, TimestampsToReturn.Neither, new ArrayOf<ReadValueId>(request), cancel.Token)
            .AsTask().GetAwaiter().GetResult();
        var results = response.Results.ToArray() ?? [];
        if (results.Length != nodes.Count)
        {
            throw new InvalidOperationException($"读回 {results.Length} 个结果，请求的是 {nodes.Count} 个");
        }

        return results;
    }

    /// <summary>
    /// 写数组里的一格；返回 PLC 给的结果码，整次没写成（断线、超时）抛异常
    /// </summary>
    private StatusCode WriteNode(NodeId node, int index, Variant element)
    {
        var session = RequireSession();
        var request = new WriteValue
        {
            NodeId = node,
            AttributeId = Opc.Ua.Attributes.Value,
            IndexRange = index.ToString(CultureInfo.InvariantCulture),
            Value = new DataValue(element),
        };

        using var cancel = new CancellationTokenSource(OperationTimeoutMs);
        var response = session.WriteAsync(null, new ArrayOf<WriteValue>(new[] { request }), cancel.Token)
            .AsTask().GetAwaiter().GetResult();
        return response.Results[0];
    }

    private ManagedSession RequireSession()
    {
        var session = _session;
        if (session is null || !session.Connected)
        {
            throw new InvalidOperationException("会话没连着");
        }

        return session;
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

    /// <summary>
    /// 整次读写没成（断线、超时）：撂下连接标志让基类走重连
    /// </summary>
    private void OnCommunicationFailed(string action, Exception exception)
    {
        LogHelper.Warn(this, $"{action} PLC 失败，按断线处理: {exception.Message}");
        IsConnected = false;
        DisconnectDevice();
    }

    /// <summary>
    /// 连接换代了：上一条连接读回来的值清掉——读不到（false）比拿旧值当真安全
    /// </summary>
    protected override void OnConnectionChanged(bool connected)
    {
        lock (_cacheGate)
        {
            _di = null;
            _do = null;
            _ai = null;
            _ao = null;
            _aoType = BuiltInType.Null;
        }
    }

    #endregion

    #region 缓存（按 EC ReadIntervalMs 一次往返读回四个数组）

    private readonly object _cacheGate = new();
    private bool[]? _di;
    private bool[]? _do;
    private double[]? _ai;
    private double[]? _ao;

    /// <summary>
    /// AO 数组在 PLC 里的类型：写 AO 要按这个类型写（类型不对服务端拒收），读回来过才知道
    /// </summary>
    private BuiltInType _aoType = BuiltInType.Null;

    private long _lastReadAt;

    /// <summary>
    /// 到了读的时候就一次往返把配了的数组读回来；某个读不成就清掉那一个、报警
    /// </summary>
    protected override void RefreshData()
    {
        long now = Environment.TickCount64;
        if (now - _lastReadAt < ReadIntervalMs)
        {
            return;
        }

        _lastReadAt = now;
        var paths = new List<string>();
        var nodes = new List<NodeId>();
        var unresolved = new List<string>();
        DataValue[] values = [];
        Exception? failure = null;
        lock (_deviceGate)
        {
            foreach (var path in new[] { DiDataPath, DoDataPath, AiDataPath, AoDataPath })
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (_nodes.TryGetValue(path, out var node))
                {
                    paths.Add(path);
                    nodes.Add(node);
                }
                else
                {
                    unresolved.Add(path);
                }
            }

            if (nodes.Count > 0)
            {
                try
                {
                    values = ReadNodes(nodes);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }
        }

        // 报警、清连接都放在锁外做
        if (failure is not null)
        {
            OnCommunicationFailed("读", failure);
            return;
        }

        foreach (var path in unresolved)
        {
            Store(path, null, "节点名认不出");
        }

        for (int i = 0; i < values.Length; i++)
        {
            Store(paths[i], values[i], string.Empty);
        }
    }

    /// <summary>
    /// 一个数组读回来了（value 为 null = 没读成，fault 是原因）：按它是 DI、DO、AI 还是 AO 换成缓存里的样子
    /// </summary>
    private void Store(string path, DataValue? value, string fault)
    {
        if (value is not null && !StatusCode.IsGood(value.Value.StatusCode))
        {
            fault = value.Value.StatusCode.ToString();
            value = null;
        }

        if (path == DiDataPath)
        {
            var bits = ToBits(value, ref fault);
            lock (_cacheGate)
            {
                _di = bits;
            }
        }

        if (path == DoDataPath)
        {
            var bits = ToBits(value, ref fault);
            lock (_cacheGate)
            {
                _do = bits;
            }
        }

        if (path == AiDataPath)
        {
            var numbers = ToNumbers(value, ref fault, out _);
            lock (_cacheGate)
            {
                _ai = numbers;
            }
        }

        if (path == AoDataPath)
        {
            var numbers = ToNumbers(value, ref fault, out var type);
            lock (_cacheGate)
            {
                _ao = numbers;
                if (numbers is not null)
                {
                    _aoType = type;
                }
            }
        }

        if (value is null || fault.Length > 0)
        {
            NoteReadFault(path, fault);
            return;
        }

        lock (_faults)
        {
            _faults.Remove(path);
        }
    }

    /// <summary>
    /// 读失败报警；同一个节点同样的原因只记一次日志
    /// </summary>
    private void NoteReadFault(string path, string fault)
    {
        RaiseAlarm(PlcDataErrorAlarm);
        lock (_faults)
        {
            if (_faults.TryGetValue(path, out var last) && last == fault)
            {
                return;
            }

            _faults[path] = fault;
        }

        LogHelper.Warn(this, $"读 {path} 没读成（{fault}）");
    }

    private static bool[]? ToBits(DataValue? value, ref string fault)
    {
        if (value is null)
        {
            return null;
        }

        var variant = value.Value.WrappedValue;
        if (variant.TypeInfo.BuiltInType != BuiltInType.Boolean || !variant.TypeInfo.IsArray)
        {
            fault = $"不是 BOOL 数组（读回来是 {variant.TypeInfo}）";
            return null;
        }

        return variant.GetBooleanArray().ToArray() ?? [];
    }

    private static double[]? ToNumbers(DataValue? value, ref string fault, out BuiltInType type)
    {
        type = BuiltInType.Null;
        if (value is null)
        {
            return null;
        }

        var variant = value.Value.WrappedValue;
        type = variant.TypeInfo.BuiltInType;
        if (!variant.TypeInfo.IsArray)
        {
            fault = $"不是数组（读回来是 {variant.TypeInfo}）";
            return null;
        }

        switch (type)
        {
            case BuiltInType.SByte:
                return ToDoubles(variant.GetSByteArray().ToArray());
            case BuiltInType.Byte:
                return ToDoubles(variant.GetByteArray().ToArray());
            case BuiltInType.Int16:
                return ToDoubles(variant.GetInt16Array().ToArray());
            case BuiltInType.UInt16:
                return ToDoubles(variant.GetUInt16Array().ToArray());
            case BuiltInType.Int32:
                return ToDoubles(variant.GetInt32Array().ToArray());
            case BuiltInType.UInt32:
                return ToDoubles(variant.GetUInt32Array().ToArray());
            case BuiltInType.Int64:
                return ToDoubles(variant.GetInt64Array().ToArray());
            case BuiltInType.UInt64:
                return ToDoubles(variant.GetUInt64Array().ToArray());
            case BuiltInType.Float:
                return ToDoubles(variant.GetFloatArray().ToArray());
            case BuiltInType.Double:
                return variant.GetDoubleArray().ToArray() ?? [];
            default:
                fault = $"不是数值数组（读回来是 {variant.TypeInfo}）";
                return null;
        }
    }

    private static double[] ToDoubles<T>(T[]? values) where T : INumber<T>
    {
        if (values is null)
        {
            return [];
        }

        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = double.CreateChecked(values[i]);
        }

        return result;
    }

    #endregion

    #region DI / DO / AI / AO

    /// <summary>
    /// 读一个 DI 点。读不到返回 false（没连上、没配、没读成、下标超出数组），值只在返回 true 时有意义
    /// </summary>
    public bool TryReadDi(int index, out bool on)
    {
        lock (_cacheGate)
        {
            return TryTake(_di, index, out on);
        }
    }

    /// <summary>
    /// 回读一个 DO 点当前的输出状态（DO 数组也按间隔读回来）
    /// </summary>
    public bool TryReadDo(int index, out bool on)
    {
        lock (_cacheGate)
        {
            return TryTake(_do, index, out on);
        }
    }

    /// <summary>
    /// 读一个 AI 点的原始值。标定（原始值 → 工程值）是 AI 组件的事
    /// </summary>
    public bool TryReadAi(int index, out double value)
    {
        lock (_cacheGate)
        {
            return TryTake(_ai, index, out value);
        }
    }

    /// <summary>
    /// 回读一个 AO 点当前的输出值
    /// </summary>
    public bool TryReadAo(int index, out double value)
    {
        lock (_cacheGate)
        {
            return TryTake(_ao, index, out value);
        }
    }

    /// <summary>
    /// 直接写一个 DO 点（只写数组里这一格）；不改回读缓存，实际状态等下一次读回来确认
    /// </summary>
    public bool WriteDo(int index, bool on)
    {
        if (!IsConnected || index < 0 || string.IsNullOrWhiteSpace(DoDataPath))
        {
            return false;
        }

        lock (_cacheGate)
        {
            // 读回来过就按数组长度挡越界；还没读到过交给服务端判，越界它会拒
            if (_do is not null && index >= _do.Length)
            {
                return false;
            }
        }

        return WriteElement(DoDataPath, index, new Variant(new ArrayOf<bool>(new[] { on })));
    }

    /// <summary>
    /// 直接写一个 AO 点（只写数组里这一格），按 PLC 里数组的类型换算；还没读回来过、不知道类型时写不了
    /// </summary>
    public bool WriteAo(int index, double value)
    {
        if (!IsConnected || index < 0 || string.IsNullOrWhiteSpace(AoDataPath) || !double.IsFinite(value))
        {
            return false;
        }

        BuiltInType type;
        lock (_cacheGate)
        {
            if (_ao is null || index >= _ao.Length)
            {
                return false;
            }

            type = _aoType;
        }

        if (!OpcUaHelper.TryMakeElement(type, value, out var element))
        {
            LogHelper.Warn(this, $"写 AO{index} = {value} 没写：超出 PLC 里 {type} 的范围");
            return false;
        }

        return WriteElement(AoDataPath, index, element);
    }

    private bool WriteElement(string path, int index, Variant element)
    {
        StatusCode status = StatusCodes.Good;
        Exception? failure = null;
        lock (_deviceGate)
        {
            if (!IsConnected || !_nodes.TryGetValue(path, out var node))
            {
                return false;
            }

            try
            {
                status = WriteNode(node, index, element);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        // 清连接放在锁外做
        if (failure is not null)
        {
            OnCommunicationFailed($"写 {path}[{index}]", failure);
            return false;
        }

        if (StatusCode.IsGood(status))
        {
            return true;
        }

        LogHelper.Warn(this, $"写 {path}[{index}] 被 PLC 拒了（{status}）");
        return false;
    }

    private static bool TryTake<T>(T[]? values, int index, out T value)
    {
        value = default!;
        if (values is null || index < 0 || index >= values.Length)
        {
            return false;
        }

        value = values[index];
        return true;
    }

    #endregion
}
