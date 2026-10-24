using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Service.Ai;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Tools;

// AI 冒烟：AI 组件（machine.xml 的 Ai 节点）配错开机就报（地址、模型名、同时几个）；按 IAi 进容器；
// 解释报警：报警不在当前报警里回 ai.alarm_not_found、不问模型；交给模型的材料（报警的说明和处理办法、来源下面的信号前后怎么变的和现在的值、
// 最近的告警和错误日志），EC 限条数，回答的语言跟界面，回答带它看了哪些数据；模型回空的、出错、超时（含排队）的错误码；同时只让 machine.xml MaxConcurrent 个在模型上算；
// 服务（没配回 ai.not_installed、状态带模型名、回答转成 AiAnswerDto）；
// 真走一遍微软 Microsoft.Extensions.AI 的 OpenAI 格式适配：本机起一个假的模型服务（HttpListener），看请求的路径、模型名、消息，回答原样回来；服务停了当场报错不重试。
// 假的模型（IChatClient）、假的日志和数据曲线，不连真模型。

SmokeIoc.Reset();

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

const string Source = "Chamber.Chamber1.Door";
const string Code = "TimeoutAlarm";
string maxTokensField = string.Empty;

var previousEc = IocHelper.GetService<IEc>();
var previousAlarm = IocHelper.GetService<IAlarm>();
try
{
    // 0. 内存 EC、报警组件、映射组件；假日志、假数据曲线
    SmokeIoc.Use<IEc>(new EcComponent());
    var alarms = new AlarmComponent();
    SmokeIoc.Use<IAlarm>(alarms);
    SmokeIoc.Use<IMapper>(new MapperComponent());
    var logs = new FakeLogs();
    SmokeIoc.Use<ILogComponent>(logs);
    var chart = new FakeChart();
    SmokeIoc.Use<IDataChart>(chart);

    // 1. 配错开机就报：地址没配或不是 http / https、没配模型名、同时几个配成 0；配对了能装上
    string? LoadError(params ValueConfig[] values)
    {
        try
        {
            ComponentLoader.Load([Node("Ai", typeof(SmokeAi), values)]);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    string? error = LoadError(Value("Model", "m"));
    Check(error is not null && error.Contains("Endpoint", StringComparison.Ordinal), "没配模型服务地址：装配就报 Endpoint");
    error = LoadError(Value("Endpoint", "ftp://192.168.10.50/v1"), Value("Model", "m"));
    Check(error is not null && error.Contains("ftp://192.168.10.50/v1", StringComparison.Ordinal), "地址不是 http / https：装配就报，带上配的值");
    error = LoadError(Value("Endpoint", "http://192.168.10.50:8080/v1"));
    Check(error is not null && error.Contains("Model", StringComparison.Ordinal), "没配模型名：装配就报 Model");
    error = LoadError(Value("Endpoint", "http://192.168.10.50:8080/v1"), Value("Model", "m"), Value("MaxConcurrent", "0"));
    Check(error is not null && error.Contains("MaxConcurrent", StringComparison.Ordinal), "同时几个配成 0：装配就报");
    Check(LoadError(Value("Endpoint", "http://192.168.10.50:8080/v1"), Value("Model", "m")) is null, "地址、模型名配对了能装上");

    // 2. 装上：AI 组件 + 报警来源（Chamber.Chamber1.Door，分组节点进路径）；组件初始化建好（假的）模型客户端，Bind 取报警、日志、数据曲线
    var fake = new FakeChat { Reply = "  可能是开到位传感器坏了。\n先查 Opened 信号的接线。  " };
    SmokeAi.Next = fake;
    var roots = SmokeIoc.Load(
    [
        Node("Ai", typeof(SmokeAi), Value("Endpoint", "http://192.168.10.50:8080/v1"), Value("Model", "qwen-test")),
        Group("Chamber", Group("Chamber1", Node("Door", typeof(AlarmProbe)))),
    ]);
    var ai = (SmokeAi)roots[0];
    var door = roots[1].FullPath == Source ? (AlarmProbe)roots[1] : throw new InvalidOperationException("FAIL: 报警来源的路径不对：" + roots[1].FullPath);
    Check(ai.InitComponent(), "AI 组件初始化：只建对象，不连网");
    ComponentLoader.BindAll(roots);
    Check(ReferenceEquals(IocHelper.GetService<IAi>(), ai) && ai.Model == "qwen-test", "按 IAi 找得到 AI 组件，模型名照 machine.xml");
    Check(ai.TimeoutMs == 120000 && ai.LogCount == 50 && ai.SignalWindowSeconds == 120 && ai.SignalCount == 20
        && ai.MaxOutputTokens == 1000 && Math.Abs(ai.Temperature - 0.2) < 1e-9 && ai.MaxConcurrent == 1,
        "默认：超时 120 s、50 条日志、前后各 120 s、20 个信号、回答 1000 token、随机程度 0.2、同时 1 个");

    // 3. 报警不在当前报警里：回 ai.alarm_not_found [来源, 报警码]，不问模型
    var result = await ai.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None);
    Check(!result.IsSuccess && result.ErrorMessage == ErrorCodes.AiAlarmNotFound && result.Args.SequenceEqual([Source, Code]) && fake.Requests.Count == 0,
        "报警还没报：回 ai.alarm_not_found [来源, 报警码]，没问模型");

    // 4. 报出报警，摆好前后的信号和最近的日志：交给模型的材料
    door.Raise();
    var raised = alarms.ActiveAlarms.Single(item => item.SourcePath == Source && item.AlarmCode == Code);
    long raisedMs = raised.RaisedAt.ToUnixTimeMilliseconds();
    chart.Times = [raisedMs - 3000, raisedMs - 2000, raisedMs - 1000, raisedMs];
    chart.Values["Chamber__Chamber1__Door__DoOpen"] = [0, 1, 1, 1];
    chart.Values["Chamber__Chamber1__Door__Opened"] = [0, 0, 0, 0];
    chart.Values["Chamber__Chamber1__Door__Closed"] = [1, 1, 0, 0];
    chart.Values["Chamber__Chamber1__Door__Pressure"] = [500, 480, 300, 310];
    logs.Items.Add(new LogDto { Time = DateTime.Now.AddSeconds(-30), Level = "Warn", Module = "Chamber1", Message = "门气缸开到位等待中" });
    logs.Items.Add(new LogDto { Time = DateTime.Now.AddSeconds(-20), Level = "Error", Module = "Chamber1", Message = "Door 开到位超时" });
    logs.Items.Add(new LogDto { Time = DateTime.Now.AddSeconds(-10), Level = "Warn", Module = "Robot", Message = "等待腔门打开" });

    result = await ai.ExplainAlarmAsync(Source.ToLowerInvariant(), Code, "zh-CN", CancellationToken.None);
    var answer = result.Result;
    Check(result.IsSuccess && answer is not null && answer.Text == "可能是开到位传感器坏了。\n先查 Opened 信号的接线。" && answer.Model == "qwen-test",
        "报警还在：问了模型，回答去掉前后空白原样带回，带模型名（来源不分大小写）");
    Check(answer!.Signals.SequenceEqual(["Chamber__Chamber1__Door__DoOpen", "Chamber__Chamber1__Door__Opened", "Chamber__Chamber1__Door__Closed", "Chamber__Chamber1__Door__Pressure"])
        && answer.LogCount == 3, "回答带它看了哪些数据：来源下面的 4 个信号（按 machine.xml 先后，DoorLock、Spin 不算），3 条日志");
    Check(chart.LastNames.SequenceEqual(answer.Signals) && Math.Abs((raised.RaisedAt.LocalDateTime - chart.LastStart).TotalSeconds - 120) < 1
        && chart.LastEnd <= DateTime.Now && chart.LastEnd >= raised.RaisedAt.LocalDateTime,
        "数据曲线按这 4 个信号查报警前 120 s 到现在（还没过报警后 120 s）");

    var request = fake.Requests[^1];
    string instructions = request[0].Text;
    string material = request[1].Text;
    Check(request.Count == 2 && request[0].Role == ChatRole.System && request[1].Role == ChatRole.User, "两条消息：规矩（system）+ 材料（user）");
    Check(instructions.Contains("不要编造", StringComparison.Ordinal) && instructions.Contains("不要建议跳过联锁", StringComparison.Ordinal)
        && instructions.EndsWith("用简体中文回答。", StringComparison.Ordinal), "规矩：只按材料说、不建议跳过联锁；界面中文就用中文答");
    Check(material.Contains("来源：Chamber.Chamber1.Door", StringComparison.Ordinal) && material.Contains("报警：开/关到位超时（TimeoutAlarm）", StringComparison.Ordinal)
        && material.Contains("说明：开门命令发出后到位信号没在时间内亮", StringComparison.Ordinal) && material.Contains("处理办法：检查到位传感器和气压", StringComparison.Ordinal),
        "材料里有报警本身：来源、报警、说明、处理办法");
    string LineOf(string name)
    {
        return material.Split('\n').Single(line => line.StartsWith(name + "（", StringComparison.Ordinal));
    }

    string doOpen = LineOf("Chamber__Chamber1__Door__DoOpen");
    Check(doOpen.Contains("（DO，开门阀）", StringComparison.Ordinal) && doOpen.Contains("起是 0", StringComparison.Ordinal)
        && doOpen.Contains("变成 1", StringComparison.Ordinal) && doOpen.TrimEnd().EndsWith("现在 1", StringComparison.Ordinal),
        "数字量：先写起始值，再列每次变化，最后写现在的值");
    string opened = LineOf("Chamber__Chamber1__Door__Opened");
    Check(opened.Contains("起是 0", StringComparison.Ordinal) && !opened.Contains("变成", StringComparison.Ordinal) && opened.TrimEnd().EndsWith("现在 0", StringComparison.Ordinal),
        "开到位一直没亮：只写起始值，没有变化");
    string closed = LineOf("Chamber__Chamber1__Door__Closed");
    Check(closed.Contains("起是 1", StringComparison.Ordinal) && closed.Contains("变成 0", StringComparison.Ordinal), "关到位灭了：列出变化");
    string pressure = LineOf("Chamber__Chamber1__Door__Pressure");
    Check(pressure.Contains("（AI，kPa，气压）", StringComparison.Ordinal) && pressure.Contains("最小 300，最大 500，报警时 310", StringComparison.Ordinal)
        && pressure.TrimEnd().EndsWith("现在 305.5", StringComparison.Ordinal), "模拟量：最小、最大、报警那一刻的值，带单位，现在的值");
    Check(!material.Contains("DoorLock", StringComparison.Ordinal) && !material.Contains("Spin", StringComparison.Ordinal),
        "别的部件的信号不带（DoorLock 只是名字开头像，Spin 是别的部件）");
    Check(material.Contains("Error Chamber1：Door 开到位超时", StringComparison.Ordinal) && material.Contains("Warn Robot：等待腔门打开", StringComparison.Ordinal),
        "材料里有最近的告警、错误日志");
    Check(fake.LastOptions is not null && fake.LastOptions.MaxOutputTokens == 1000 && fake.LastOptions.Temperature == 0.2f,
        "回答最长、随机程度照 EC 给模型");

    // 5. 回答的语言跟界面：英文界面用英文答，没给语言按中文
    await ai.ExplainAlarmAsync(Source, Code, "en-US", CancellationToken.None);
    Check(fake.Requests[^1][0].Text.EndsWith("Answer in English.", StringComparison.Ordinal), "界面英文：让模型用英文答");
    await ai.ExplainAlarmAsync(Source, Code, string.Empty, CancellationToken.None);
    Check(fake.Requests[^1][0].Text.EndsWith("用简体中文回答。", StringComparison.Ordinal), "没给语言：按中文");

    // 6. EC 限条数：日志只带最新的 1 条，信号只带前 2 个；信号配 0 就不查数据曲线
    ai.LogCount = 1;
    ai.SignalCount = 2;
    answer = (await ai.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None)).Result;
    material = fake.Requests[^1][1].Text;
    Check(answer is not null && answer.LogCount == 1 && answer.Signals.Count == 2 && material.Contains("等待腔门打开", StringComparison.Ordinal)
        && !material.Contains("门气缸开到位等待中", StringComparison.Ordinal) && !material.Contains("Pressure", StringComparison.Ordinal),
        "EC 日志 1 条、信号 2 个：只带最新那条日志、前 2 个信号");
    int queries = chart.QueryCount;
    ai.SignalCount = 0;
    ai.LogCount = 0;
    answer = (await ai.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None)).Result;
    material = fake.Requests[^1][1].Text;
    Check(answer is not null && answer.LogCount == 0 && answer.Signals.Count == 0 && chart.QueryCount == queries
        && !material.Contains("【报警前后的信号】", StringComparison.Ordinal) && !material.Contains("【最近的告警、错误日志】", StringComparison.Ordinal),
        "信号、日志都配 0：只带报警本身，不查数据曲线");
    ai.SignalCount = 20;
    ai.LogCount = 50;

    // 7. 模型回空的、出错：错误码
    fake.Reply = "   ";
    result = await ai.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None);
    Check(!result.IsSuccess && result.ErrorMessage == ErrorCodes.AiEmptyAnswer, "模型回了空的：ai.empty_answer");
    fake.Reply = "可能是开到位传感器坏了。";
    fake.Error = new HttpRequestException("由于目标计算机积极拒绝，无法连接。");
    result = await ai.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None);
    Check(!result.IsSuccess && result.ErrorMessage == ErrorCodes.AiFailed && result.Args.SequenceEqual(["由于目标计算机积极拒绝，无法连接。"]),
        "模型服务出错：ai.failed [原因]");
    fake.Error = null;

    // 8. 超时：模型一直不回，超过 EC TimeoutMs 回 ai.timeout [ms]，排队的位置放回去（下一次照样能问）
    ai.TimeoutMs = 1000;
    fake.Gate = new TaskCompletionSource();
    var watch = Stopwatch.StartNew();
    result = await ai.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None);
    Check(!result.IsSuccess && result.ErrorMessage == ErrorCodes.AiTimeout && result.Args.SequenceEqual(["1000"]) && watch.ElapsedMilliseconds >= 900,
        "模型一直不回：等满 EC TimeoutMs 回 ai.timeout [ms]");
    fake.Gate = null;
    ai.TimeoutMs = 120000;
    result = await ai.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None);
    Check(result.IsSuccess, "超时以后照样能问（排队的位置放回去了）");

    // 9. 排队：machine.xml MaxConcurrent = 1 时两个一起问，模型上同时只算一个，后一个等前一个答完；超时把排队也算在内
    fake.MaxRunning = 0;
    var gate = new TaskCompletionSource();
    fake.Gate = gate;
    int before = fake.Requests.Count;
    var first = Task.Run(() => ai.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None));
    var second = Task.Run(() => ai.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None));
    var waitFirst = Stopwatch.StartNew();
    while (fake.Requests.Count == before && waitFirst.ElapsedMilliseconds < 3000)
    {
        Thread.Sleep(10);
    }

    Thread.Sleep(200);
    Check(fake.Requests.Count == before + 1 && fake.Running == 1, "两个一起问：只有一个在模型上算，另一个排着");
    gate.SetResult();
    var both = await Task.WhenAll(first, second);
    Check(both.All(item => item.IsSuccess) && fake.Requests.Count == before + 2 && fake.MaxRunning == 1, "放开以后两个都答完，模型上同时最多一个");
    fake.Gate = null;

    // 10. 服务：状态带模型名；回答转成 AiAnswerDto；报警没了原样回错误码；没配 AI 回 ai.not_installed
    var service = new AiService();
    var response = await service.GetStatusAsync(new RpcRequest());
    Check(response.Success && response.DeserializeData<AiStatusDto>().Model == "qwen-test", "服务：配了 AI，状态带模型名");
    response = await service.ExplainAlarmAsync(new AiAlarmRequest { Source = Source, Code = Code });
    var dto = response.Success ? response.DeserializeData<AiAnswerDto>() : null;
    Check(dto is not null && dto.Text == "可能是开到位传感器坏了。" && dto.Model == "qwen-test" && dto.Signals.Count == 4 && dto.LogCount == 3,
        "服务：回答转成 AiAnswerDto（正文、模型、信号、日志条数）");
    Check(fake.Requests[^1][0].Text.EndsWith("用简体中文回答。", StringComparison.Ordinal), "服务：没装整机（没有界面语言）按中文答");
    response = await service.ExplainAlarmAsync(new AiAlarmRequest { Source = Source, Code = "Nope" });
    Check(!response.Success && response.Code == ErrorCodes.AiAlarmNotFound && response.Args.SequenceEqual([Source, "Nope"]), "服务：报警没了原样回 ai.alarm_not_found");
    SmokeIoc.Use<IAi>(null);
    var noAi = new AiService();
    response = await noAi.GetStatusAsync(new RpcRequest());
    var explainNoAi = await noAi.ExplainAlarmAsync(new AiAlarmRequest { Source = Source, Code = Code });
    Check(!response.Success && response.Code == ErrorCodes.AiNotInstalled && explainNoAi.Code == ErrorCodes.AiNotInstalled, "服务：没配 AI 都回 ai.not_installed");

    // 11. 真走一遍微软的 OpenAI 格式适配：本机假模型服务收到 POST /v1/chat/completions，模型名、两条消息、占位密钥都对，回答原样回来
    using (var server = new FakeModelServer("门到位传感器可能坏了"))
    {
        var real = (AiComponent)SmokeIoc.Load([Node("Ai", typeof(AiComponent), Value("Endpoint", server.Endpoint), Value("Model", "test-model"))]).Single();
        real.InitComponent();
        real.Bind();
        result = await real.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None);
        Check(result.IsSuccess && result.Result is not null && result.Result.Text == "门到位传感器可能坏了", "真走 HTTP：回答原样回来");
        Check(server.Path == "/v1/chat/completions" && server.Authorization == "Bearer no-key", "请求发到地址后面的 /chat/completions，没配密钥带占位的");
        using var body = JsonDocument.Parse(server.Body ?? "{}");
        var root = body.RootElement;
        var messages = root.GetProperty("messages");
        Check(root.GetProperty("model").GetString() == "test-model" && messages.GetArrayLength() == 2
            && messages[0].GetProperty("role").GetString() == "system" && messages[1].GetProperty("role").GetString() == "user"
            && (messages[1].GetProperty("content").GetString() ?? string.Empty).Contains("开/关到位超时", StringComparison.Ordinal),
            "请求里模型名照 machine.xml、两条消息（规矩 + 材料）");
        foreach (string field in new[] { "max_completion_tokens", "max_tokens" })
        {
            if (root.TryGetProperty(field, out var value) && value.GetInt32() == 1000)
            {
                maxTokensField = field;
            }
        }

        Check(maxTokensField.Length > 0, "回答最长 1000 token 发给了模型服务");
        server.Stop();
        watch.Restart();
        result = await real.ExplainAlarmAsync(Source, Code, "zh-CN", CancellationToken.None);
        Check(!result.IsSuccess && result.ErrorMessage == ErrorCodes.AiFailed && watch.ElapsedMilliseconds < 5000, "模型服务停了：当场回 ai.failed，不重试干等");
        real.OnStopping();
    }
}
finally
{
    SmokeIoc.Use<IEc>(previousEc);
    SmokeIoc.Use<IAlarm>(previousAlarm);
}

Console.WriteLine($"PASS: {checks} ai checks (machine.xml errors at startup, found by IAi, alarm not active, the material handed to the model "
    + "(alarm text, description and solution, related signals before and after the alarm with their changes and current values, recent warn / error logs), "
    + "EC limits, answer language following the UI, answer with what it looked at, empty answer / service error / timeout codes, one request at a time on the model, "
    + $"the service (status, answer DTO, not installed), and a real HTTP round trip through Microsoft.Extensions.AI's OpenAI adapter (max tokens sent as {maxTokensField}), no retry when the model service is down)");

static ValueConfig Value(string name, string value)
{
    return new ValueConfig { Name = name, Value = value };
}

static ModuleConfig Node(string name, Type type, params ValueConfig[] values)
{
    return new ModuleConfig { Name = name, Type = type.FullName, Values = [.. values] };
}

// 分组节点（不写 Type）：不生成组件，名字进路径
static ModuleConfig Group(string name, params ModuleConfig[] children)
{
    return new ModuleConfig { Name = name, Children = [.. children] };
}

/// <summary>
/// 冒烟用的 AI 组件：模型客户端换成假的
/// </summary>
[Component(description: "冒烟用 AI 组件")]
public sealed class SmokeAi : AiComponent
{
    /// <summary>
    /// 组件初始化时用的假模型
    /// </summary>
    public static IChatClient? Next { get; set; }

    protected override IChatClient CreateChatClient()
    {
        return Next ?? throw new InvalidOperationException("FAIL: 没给假模型");
    }
}

/// <summary>
/// 冒烟用的报警来源：腔门，报警的说明、处理办法照真的写
/// </summary>
[Component(description: "冒烟用报警来源")]
public sealed class AlarmProbe : ComponentBase
{
    [Alarm("开/关到位超时", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Alarm1,
        Description = "开门命令发出后到位信号没在时间内亮",
        Solution = "检查到位传感器和气压")]
    public string TimeoutAlarm = nameof(TimeoutAlarm);

    /// <summary>
    /// 报这条报警
    /// </summary>
    public void Raise()
    {
        RaiseAlarm(TimeoutAlarm);
    }
}

/// <summary>
/// 假的模型：记下每次收到的消息和选项，回固定的话；可以让它出错、或者卡住等放行
/// </summary>
public sealed class FakeChat : IChatClient
{
    private int _running;

    /// <summary>
    /// 每次收到的消息
    /// </summary>
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    /// <summary>
    /// 最近一次的选项
    /// </summary>
    public ChatOptions? LastOptions { get; private set; }

    /// <summary>
    /// 回的话
    /// </summary>
    public string Reply { get; set; } = string.Empty;

    /// <summary>
    /// 不为 null 就抛它
    /// </summary>
    public Exception? Error { get; set; }

    /// <summary>
    /// 不为 null 就等它放行再回
    /// </summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>
    /// 正在算的个数
    /// </summary>
    public int Running => Volatile.Read(ref _running);

    /// <summary>
    /// 同时在算的最多几个
    /// </summary>
    public int MaxRunning { get; set; }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        int running = Interlocked.Increment(ref _running);
        try
        {
            lock (Requests)
            {
                Requests.Add(messages.ToList());
                LastOptions = options;
                MaxRunning = Math.Max(MaxRunning, running);
            }

            var gate = Gate;
            if (gate is not null)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            var error = Error;
            if (error is not null)
            {
                throw error;
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply));
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("AI 组件不用流式");
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// 假的日志组件：最近的日志由冒烟摆
/// </summary>
public sealed class FakeLogs : ILogComponent
{
    /// <summary>
    /// 最近的日志（最旧的在前）
    /// </summary>
    public List<LogDto> Items { get; } = [];

    public int HistoryQueryMaxCount => 1000;

    public int RecentLogCount => 200;

    public int RealtimeDisplayMaxCount => 2000;

    public int LogBarDisplayMaxCount => 500;

    public IReadOnlyList<LogDto> Recent()
    {
        return Items.ToArray();
    }
}

/// <summary>
/// 假的数据曲线：信号表固定（门下面 4 个、名字开头像的 DoorLock、别的部件 Spin），曲线由冒烟摆，记下最近一次查了什么
/// </summary>
public sealed class FakeChart : IDataChart
{
    /// <summary>
    /// 查回去的时间轴（UTC 毫秒）
    /// </summary>
    public long[] Times { get; set; } = [];

    /// <summary>
    /// 每个信号的曲线
    /// </summary>
    public Dictionary<string, float?[]> Values { get; } = [];

    /// <summary>
    /// 最近一次查的信号
    /// </summary>
    public IReadOnlyList<string> LastNames { get; private set; } = [];

    /// <summary>
    /// 最近一次查的起点
    /// </summary>
    public DateTime LastStart { get; private set; }

    /// <summary>
    /// 最近一次查的终点
    /// </summary>
    public DateTime LastEnd { get; private set; }

    /// <summary>
    /// 一共查了几次
    /// </summary>
    public int QueryCount { get; private set; }

    public int SampleIntervalMs => 1000;

    public IReadOnlyList<DataSignal> Signals { get; } =
    [
        new DataSignal("Chamber__Chamber1__DoorLock", true, "DI", "DI90", string.Empty, "门锁", () => 1),
        new DataSignal("Chamber__Chamber1__Door__DoOpen", true, "DO", "DO100", string.Empty, "开门阀", () => 1),
        new DataSignal("Chamber__Chamber1__Door__Opened", true, "DI", "DI100", string.Empty, string.Empty, () => 0),
        new DataSignal("Chamber__Chamber1__Door__Closed", true, "DI", "DI101", string.Empty, string.Empty, () => 0),
        new DataSignal("Chamber__Chamber1__Door__Pressure", false, "AI", "AI100", "kPa", "气压", () => 305.5),
        new DataSignal("Chamber__Chamber1__Spin__Speed", false, "SV", string.Empty, "rpm", string.Empty, () => 0),
    ];

    public IObservable<DataRecord> Records => Observable.Empty<DataRecord>();

    public int QueryMaxSignals => 8;

    public string ScreenshotDirectory => string.Empty;

    public DataQueryResult Query(IReadOnlyList<string> names, DateTime start, DateTime end, CancellationToken token)
    {
        LastNames = names.ToList();
        LastStart = start;
        LastEnd = end;
        QueryCount++;
        var series = new List<DataQuerySeries>();
        foreach (string name in names)
        {
            if (Values.TryGetValue(name, out var values))
            {
                series.Add(new DataQuerySeries { Name = name, Values = values });
            }
        }

        return new DataQueryResult { Times = Times, Series = series };
    }
}

/// <summary>
/// 本机假的模型服务（兼容 OpenAI 接口格式）：收一个请求就记下路径、密钥和请求体，回固定的回答
/// </summary>
public sealed class FakeModelServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _reply;

    public FakeModelServer(string reply)
    {
        _reply = reply;
        int port = FreePort();
        Endpoint = $"http://localhost:{port}/v1";
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _ = Task.Run(Serve);
    }

    /// <summary>
    /// 填进 machine.xml Endpoint 的地址
    /// </summary>
    public string Endpoint { get; }

    /// <summary>
    /// 最近一次请求的路径
    /// </summary>
    public string? Path { get; private set; }

    /// <summary>
    /// 最近一次请求带的密钥头
    /// </summary>
    public string? Authorization { get; private set; }

    /// <summary>
    /// 最近一次请求体
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>
    /// 停掉（之后的请求连不上）
    /// </summary>
    public void Stop()
    {
        if (_listener.IsListening)
        {
            _listener.Stop();
        }
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
    }

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }

            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                Body = await reader.ReadToEndAsync();
            }

            Path = context.Request.Url?.AbsolutePath;
            Authorization = context.Request.Headers["Authorization"];
            string json = JsonSerializer.Serialize(new
            {
                id = "chatcmpl-smoke",
                @object = "chat.completion",
                created = 1760000000,
                model = "test-model",
                choices = new[]
                {
                    new { index = 0, message = new { role = "assistant", content = _reply }, finish_reason = "stop" },
                },
                usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 },
            });
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
