using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using xyz.Components;
using xyz.Components.Components;
using xyz.Components.Interfaces;

// OPC UA PLC 冒烟：本机用官方 SDK 起一个假 PLC（DI / DO 是 BOOL 数组，AI 是 REAL 数组，AO 是 INT 数组），OpcUaPlcComponent 走真的网络连它：
// 只实现 IPlcIo、不算 IPlcBlock；按 nsu= 节点名连上、一次读回四个数组；DI/AI 读值（REAL 不丢小数）、下标越界读不到；
// DO 只写一格、读回来才变；AO 按 PLC 里的 INT 四舍五入写、超范围 / 非数字不写；节点名认不出、类型不对都报"数据读写失败"；
// PLC 停了按断线处理（读不到、掉线报警），PLC 重启后命名空间编号变了，按 nsu= 重连回来照样读写；Close 以后不再连。
// EC、报警都是内存里的，不读写配置文件。

SmokeIoc.Reset();
var ec = new EcComponent();
SmokeIoc.Use<IEc>(ec);
var alarms = new AlarmComponent();
SmokeIoc.Use<IAlarm>(alarms);

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

bool HasAlarm(string code)
{
    return alarms.ActiveAlarms.Any(alarm => alarm.AlarmCode == code);
}

bool TickUntil(ProbeOpcUaPlc plc, Func<bool> done, int timeoutMs = 15000)
{
    var deadline = Environment.TickCount64 + timeoutMs;
    while (Environment.TickCount64 < deadline)
    {
        plc.Tick();
        if (done())
        {
            return true;
        }

        Thread.Sleep(20);
    }

    return false;
}

const int PlcPort = 48410;
const string PlcNamespace = "urn:xyz:smoke:plc";

Check(typeof(IPlcIo).IsAssignableFrom(typeof(OpcUaPlcComponent)), "OPC UA PLC 给 IO 组件用（IPlcIo）");
Check(!typeof(IPlcBlock).IsAssignableFrom(typeof(OpcUaPlcComponent)), "OPC UA PLC 不支持轴的数据块，不算 IPlcBlock（容器里轴不会接到它）");

var server = await FakePlc.StartAsync(PlcPort, [PlcNamespace]);
var plc = new ProbeOpcUaPlc
{
    Host = "localhost",
    Port = PlcPort,
    DiDataPath = $"nsu={PlcNamespace};s=DigIn",
    DoDataPath = $"nsu={PlcNamespace};s=DigOut",
    AiDataPath = $"nsu={PlcNamespace};s=AnaIn",
    AoDataPath = $"nsu={PlcNamespace};s=AnaOut",
};
plc.ReadIntervalMs = 10;
plc.OperationTimeoutMs = 2000;
plc.ReconnectIntervalMs = 500;
plc.ReconnectMaxCount = 20;
plc.OfflineDebounceMs = 0;

try
{
    // 1) 连上、读回
    Check(plc.Open() && plc.IsConnected, "按 nsu= 节点名连上假 PLC");
    Check(!plc.TryReadDi(3, out _), "连上还没读过：读不到（不是读到 0）");
    plc.Tick();
    Check(plc.TryReadDi(3, out var on) && on, "DI 3 读回通");
    Check(plc.TryReadDi(4, out on) && !on, "DI 4 读回断");
    Check(!plc.TryReadDi(16, out _) && !plc.TryReadDi(-1, out _), "DI 下标超出数组读不到");
    Check(plc.TryReadAi(2, out var value) && value == 12.75, "AI 是 REAL：12.75 不丢小数");
    Check(plc.TryReadAo(1, out value) && value == 100, "AO 是 INT：回读 100");
    Check(!HasAlarm(plc.PlcDataErrorAlarm), "全读成了不报警");

    // 2) DO：只写一格，读回来才变
    Check(plc.WriteDo(5, true), "写 DO 5 通");
    Check(server.DigOut()[5] && !server.DigOut()[4] && !server.DigOut()[6], "PLC 里只有 DO 5 变了");
    Check(plc.TryReadDo(5, out on) && !on, "写完不改回读缓存，等下一次读回来");
    plc.Tick();
    Check(plc.TryReadDo(5, out on) && on, "下一次读回来 DO 5 通");
    Check(!plc.WriteDo(16, true) && !plc.WriteDo(-1, true), "DO 下标超出数组不写");

    // 3) AO：按 PLC 里的 INT 写
    Check(plc.WriteAo(2, 3.6) && server.AnaOut()[2] == 4, "AO 写 3.6，PLC 里是 INT，四舍五入成 4");
    Check(!plc.WriteAo(2, 40000) && server.AnaOut()[2] == 4, "超出 INT 范围不写");
    Check(!plc.WriteAo(2, double.NaN) && !plc.WriteAo(2, double.PositiveInfinity), "非数字不写");
    Check(!plc.WriteAo(8, 1), "AO 下标超出数组不写");

    // 4) PLC 那边的值变了，下一次读回来跟着变
    server.SetDigIn(3, false);
    server.SetAnaIn(2, -1.5f);
    plc.Tick();
    Check(plc.TryReadDi(3, out on) && !on && plc.TryReadAi(2, out value) && value == -1.5, "PLC 的值变了，读回来跟着变");

    // 5) 节点名认不出、类型不对：只是那一个读不到，报"数据读写失败"，别的照读
    var wrong = new ProbeOpcUaPlc
    {
        Host = "localhost",
        Port = PlcPort,
        DiDataPath = $"nsu={PlcNamespace};s=NoSuchNode",
        DoDataPath = $"nsu={PlcNamespace};s=AnaIn",
        AiDataPath = "nsu=urn:xyz:smoke:missing;s=AnaIn",
        AoDataPath = $"nsu={PlcNamespace};s=AnaOut",
    };
    wrong.ReadIntervalMs = 10;
    wrong.OperationTimeoutMs = 2000;
    Check(wrong.Open(), "节点名配错不影响连接");
    wrong.Tick();
    Check(!wrong.TryReadDi(0, out _), "服务端没有这个节点：读不到");
    Check(!wrong.TryReadDo(0, out _) && !wrong.WriteDo(0, true), "DO 配到了 REAL 数组：读不到");
    Check(!wrong.TryReadAi(0, out _), "命名空间不存在：认不出、读不到");
    Check(wrong.TryReadAo(1, out value) && value == 100, "配对了的那个照读");
    Check(HasAlarm(wrong.PlcDataErrorAlarm) && wrong.IsConnected, "读不成报数据读写失败，连接还在");
    wrong.Close();
    alarms.ResetAll();

    // 6) PLC 停了：按断线处理；重启以后命名空间编号变了（前面多了一个），按 nsu= 重连回来照样读写
    ushort before = server.PlcNamespaceIndex;
    await server.StopAsync();
    Check(TickUntil(plc, () => !plc.IsConnected), "PLC 停了，读不成按断线处理");
    Check(!plc.TryReadDi(4, out _) && !plc.TryReadAi(2, out _) && !plc.WriteDo(5, false), "断着读不到、写不了（缓存清了，不拿旧值当真）");
    plc.Tick();
    Check(HasAlarm(plc.PlcOfflineAlarm), "掉线报警");

    server = await FakePlc.StartAsync(PlcPort, ["urn:xyz:smoke:other", PlcNamespace]);
    Check(server.PlcNamespaceIndex != before, "重启后 PLC 的命名空间编号变了");
    Check(TickUntil(plc, () => plc.IsConnected && plc.TryReadAi(2, out _)), "后台重连回来、读回来");
    Check(plc.TryReadAi(2, out value) && value == 12.75, "编号变了照样按 nsu= 认回原来的节点");
    Check(plc.WriteDo(7, true) && server.DigOut()[7], "重连回来照样写");

    // 7) Close 以后不再连
    plc.Close();
    Check(!plc.IsConnected && !plc.TryReadDi(3, out _), "Close 以后断开、读不到");
    Thread.Sleep(700);
    plc.Tick();
    Check(!plc.IsConnected, "Close 以后不再重连");
}
finally
{
    plc.Close();
    await server.StopAsync();
}

Console.WriteLine($"PASS: {checks} opc ua checks (IPlcIo only, not IPlcBlock; connect by nsu= node ids; one round trip reads DI/DO/AI/AO arrays; "
    + "REAL AI keeps decimals; index bounds; single-element DO write confirmed by the next read; AO written as the PLC's INT with rounding and range checks; "
    + "unknown node / wrong type / missing namespace raise the data error alarm without dropping the connection; "
    + "PLC stop = offline with caches cleared; restart with a shifted namespace index reconnects and resolves nsu= again; no reconnect after Close)");

/// <summary>
/// 推得动扫描的 OPC UA PLC
/// </summary>
sealed class ProbeOpcUaPlc : OpcUaPlcComponent
{
    /// <summary>
    /// 顶替扫描线程推一拍
    /// </summary>
    public void Tick() => OnScan();
}

/// <summary>
/// 假 PLC：官方 SDK 的服务端，四个数组放在最后一个命名空间里
/// </summary>
sealed class FakePlc
{
    private ApplicationInstance _application = null!;
    private FakePlcNodeManager _nodes = null!;

    public ushort PlcNamespaceIndex => _nodes.PlcNamespaceIndex;

    public static async Task<FakePlc> StartAsync(int port, string[] namespaces)
    {
        var telemetry = DefaultTelemetry.Create(_ => { });
        var application = new ApplicationInstance(telemetry) { ApplicationName = "FakePlc", ApplicationType = ApplicationType.Server };
        string pki = Path.Combine(AppContext.BaseDirectory, "FakePlc", "pki");
        await application.Build("urn:localhost:FakePlc", "uri:FakePlc")
            .AsServer([$"opc.tcp://localhost:{port}"])
            .AddUnsecurePolicyNone()
            .AddSecurityConfiguration(
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates("CN=FakePlc", CertificateStoreType.Directory, Path.Combine(pki, "own")),
                pki)
            .SetAutoAcceptUntrustedCertificates(true)
            .CreateAsync();
        await application.CheckApplicationInstanceCertificatesAsync(true);
        var plc = new FakePlc { _application = application };
        await application.StartAsync(new FakePlcServer(telemetry, namespaces, nodes => plc._nodes = nodes));
        return plc;
    }

    public Task StopAsync()
    {
        return _application.StopAsync().AsTask();
    }

    public bool[] DigOut() => _nodes.Get("DigOut").GetBooleanArray().ToArray() ?? [];

    public short[] AnaOut() => _nodes.Get("AnaOut").GetInt16Array().ToArray() ?? [];

    public void SetDigIn(int index, bool on)
    {
        var values = _nodes.Get("DigIn").GetBooleanArray().ToArray() ?? [];
        values[index] = on;
        _nodes.Set("DigIn", new Variant(new ArrayOf<bool>(values)));
    }

    public void SetAnaIn(int index, float value)
    {
        var values = _nodes.Get("AnaIn").GetFloatArray().ToArray() ?? [];
        values[index] = value;
        _nodes.Set("AnaIn", new Variant(new ArrayOf<float>(values)));
    }
}

sealed class FakePlcServer(ITelemetryContext telemetry, string[] namespaces, Action<FakePlcNodeManager> created) : StandardServer(telemetry)
{
    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        var nodes = new FakePlcNodeManager(server, configuration, namespaces);
        created(nodes);
        return new MasterNodeManager(server, configuration, null, nodes);
    }
}

/// <summary>
/// 假 PLC 的变量：DI 16 个 BOOL（3 号通）、DO 16 个 BOOL、AI 8 个 REAL（2 号 12.75）、AO 8 个 INT（1 号 100）
/// </summary>
sealed class FakePlcNodeManager(IServerInternal server, ApplicationConfiguration configuration, string[] namespaces)
    : CustomNodeManager2(server, configuration, namespaces)
{
    private readonly Dictionary<string, BaseDataVariableState> _variables = [];

    public ushort PlcNamespaceIndex => NamespaceIndexes[NamespaceIndexes.Count - 1];

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            var digIn = new bool[16];
            digIn[3] = true;
            var anaIn = new float[8];
            anaIn[2] = 12.75f;
            var anaOut = new short[8];
            anaOut[1] = 100;
            Add("DigIn", DataTypeIds.Boolean, new Variant(new ArrayOf<bool>(digIn)));
            Add("DigOut", DataTypeIds.Boolean, new Variant(new ArrayOf<bool>(new bool[16])));
            Add("AnaIn", DataTypeIds.Float, new Variant(new ArrayOf<float>(anaIn)));
            Add("AnaOut", DataTypeIds.Int16, new Variant(new ArrayOf<short>(anaOut)));
        }
    }

    public Variant Get(string name)
    {
        lock (Lock)
        {
            return _variables[name].Value;
        }
    }

    public void Set(string name, Variant value)
    {
        lock (Lock)
        {
            var variable = _variables[name];
            variable.Value = value;
            variable.Timestamp = DateTime.UtcNow;
            variable.ClearChangeMasks(SystemContext, false);
        }
    }

    private void Add(string name, NodeId dataType, Variant value)
    {
        var variable = new BaseDataVariableState(null)
        {
            NodeId = new NodeId(name, PlcNamespaceIndex),
            BrowseName = new QualifiedName(name, PlcNamespaceIndex),
            DisplayName = new LocalizedText(name),
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            DataType = dataType,
            ValueRank = ValueRanks.OneDimension,
            AccessLevel = AccessLevels.CurrentReadOrWrite,
            UserAccessLevel = AccessLevels.CurrentReadOrWrite,
            Value = value,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow,
        };
        AddPredefinedNode(SystemContext, variable);
        _variables[name] = variable;
    }
}
