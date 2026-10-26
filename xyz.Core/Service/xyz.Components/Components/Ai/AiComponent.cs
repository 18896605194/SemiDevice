using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using OpenAI;
using xyz.Common;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// AI 助手
/// </summary>
[Component(description: "AI 助手（接厂内网的本地模型服务解释报警；只读，不动设备）")]
public class AiComponent : ComponentBase, IAi
{
    /// <summary>
    /// 本地模型服务一般不要密钥，可接口格式要求带一个：没配就带这个占位的
    /// </summary>
    private const string NoApiKey = "no-key";

    /// <summary>
    /// 一个数字量信号最多列几次变化：来回跳的信号不让它一个把材料撑满
    /// </summary>
    private const int MaxChangesPerSignal = 20;

    /// <summary>
    /// 时刻的写法（材料里都是本机时间）
    /// </summary>
    private const string ClockFormat = "HH:mm:ss";

    #region Machine

    [Machine("", "Ai", "模型服务地址：兼容 OpenAI 接口格式，带 /v1（如 http://192.168.10.50:8080/v1）", Required = true)]
    public string Endpoint { get; set; } = string.Empty;

    [Machine("", "Ai", "模型名：模型服务里叫什么就填什么", Required = true)]
    public string Model { get; set; } = string.Empty;

    [Machine("", "Ai", "访问密钥：本地模型服务一般不要，空着就行")]
    public string ApiKey { get; set; } = string.Empty;

    [Machine("1", "Ai", "同时最多几个请求在模型服务上算（本地一张显卡一般 1 个），多出来的排队")]
    public int MaxConcurrent { get; set; } = 1;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "120000", description: "一次分析最多等多久（含排队），超过算失败")]
    public int TimeoutMs
    {
        get { return GetEcInt(nameof(TimeoutMs)); }
        set { SetEcInt(nameof(TimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "0", max: "500",
        @default: "50", description: "带给模型的最近告警、错误日志最多几条（取最新的）")]
    public int LogCount
    {
        get { return GetEcInt(nameof(LogCount)); }
        set { SetEcInt(nameof(LogCount), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "s", min: "10", max: "3600",
        @default: "120", description: "看报警前后各多少秒的信号")]
    public int SignalWindowSeconds
    {
        get { return GetEcInt(nameof(SignalWindowSeconds)); }
        set { SetEcInt(nameof(SignalWindowSeconds), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "0", max: "100",
        @default: "20", description: "带给模型的相关信号最多几个（报警来源下面的，按 machine.xml 先后）")]
    public int SignalCount
    {
        get { return GetEcInt(nameof(SignalCount)); }
        set { SetEcInt(nameof(SignalCount), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "100", max: "8000",
        @default: "1000", description: "回答最长多少 token（有的本地模型服务不认这一项）")]
    public int MaxOutputTokens
    {
        get { return GetEcInt(nameof(MaxOutputTokens)); }
        set { SetEcInt(nameof(MaxOutputTokens), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, min: "0", max: "2",
        @default: "0.2", description: "回答的随机程度：越小越稳定，同一条报警答得越一样")]
    public double Temperature
    {
        get { return GetEcDouble(nameof(Temperature)); }
        set { SetEcDouble(nameof(Temperature), value); }
    }

    #endregion

    #region 开机

    private volatile IChatClient? _client;
    private volatile SemaphoreSlim? _slots;
    private IAlarm? _alarms;
    private ILogComponent? _logs;
    private IDataChart? _chart;

    /// <summary>
    /// 地址、模型名配错开机就报
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        if (!Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"machine.xml 节点 {setting.Name} 的 Endpoint=\"{Endpoint}\" 不是有效的 http / https 地址（模型服务地址，带 /v1）。");
        }

        if (Model.Trim().Length == 0)
        {
            throw new InvalidOperationException($"machine.xml 节点 {setting.Name} 没配 Model（模型服务里的模型名）。");
        }

        if (MaxConcurrent < 1)
        {
            throw new InvalidOperationException($"machine.xml 节点 {setting.Name} 的 MaxConcurrent={MaxConcurrent} 不对：至少是 1。");
        }
    }

    public override bool InitComponent()
    {
        _slots = new SemaphoreSlim(MaxConcurrent, MaxConcurrent);
        var previous = _client;
        _client = CreateChatClient();
        previous?.Dispose();
        return base.InitComponent();
    }

    public override void Bind()
    {
        _alarms = IocHelper.GetService<IAlarm>();
        _logs = IocHelper.GetService<ILogComponent>();
        _chart = IocHelper.GetService<IDataChart>();
    }

    public override void OnStopping()
    {
        var client = _client;
        _client = null;
        client?.Dispose();
    }

    protected virtual IChatClient CreateChatClient()
    {
        string key = ApiKey.Trim();
        if (key.Length == 0)
        {
            key = NoApiKey;
        }

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(Endpoint.Trim()),
            NetworkTimeout = Timeout.InfiniteTimeSpan,
            RetryPolicy = new ClientRetryPolicy(0),
        };
        return new OpenAIClient(new ApiKeyCredential(key), options).GetChatClient(Model.Trim()).AsIChatClient();
    }

    #endregion

    #region 解释报警

    /// <summary>
    /// 解释一条当前报警：排上队、整理材料、问模型，超过 EC TimeoutMs（含排队）算超时。
    /// </summary>
    /// <param name="sourcePath"></param>
    /// <param name="alarmCode"></param>
    /// <param name="language"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<HandleResult<AiAnswer>> ExplainAlarmAsync(string sourcePath, string alarmCode, string language, CancellationToken cancellationToken)
    {
        var alarm = FindAlarm(sourcePath, alarmCode);
        if (alarm is null)
        {
            return HandleResult<AiAnswer>.Fail(ErrorCodes.AiAlarmNotFound, sourcePath, alarmCode);
        }

        var client = _client;
        var slots = _slots;
        if (client is null || slots is null)
        {
            return HandleResult<AiAnswer>.Fail(ErrorCodes.AiFailed, "AI 组件还没初始化");
        }

        int timeout = TimeoutMs;
        var watch = Stopwatch.StartNew();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        bool entered = false;
        try
        {
            await slots.WaitAsync(limit.Token);
            entered = true;

            // 查数据曲线要读库，放线程池
            var material = await Task.Run(() => Collect(alarm, limit.Token), limit.Token);
            List<ChatMessage> messages =
            [
                new ChatMessage(ChatRole.System, Instructions(language)),
                new ChatMessage(ChatRole.User, material.Prompt),
            ];
            var options = new ChatOptions
            {
                MaxOutputTokens = MaxOutputTokens,
                Temperature = (float)Temperature,
            };
            var response = await client.GetResponseAsync(messages, options, limit.Token);
            string text = response.Text.Trim();
            if (text.Length == 0)
            {
                LogHelper.Info(this, $"AI 分析 {alarm.SourcePath} 的 {alarm.AlarmCode}：模型回了空的");
                return HandleResult<AiAnswer>.Fail(ErrorCodes.AiEmptyAnswer);
            }

            LogHelper.Info(this, $"AI 分析 {alarm.SourcePath} 的 {alarm.AlarmCode}：用时 {watch.ElapsedMilliseconds} ms，带了 {material.LogCount} 条日志、{material.Signals.Count} 个信号");
            return HandleResult<AiAnswer>.Success(new AiAnswer
            {
                Text = text,
                Model = Model.Trim(),
                LogCount = material.LogCount,
                Signals = material.Signals,
                ElapsedMs = watch.ElapsedMilliseconds,
            });
        }
        catch (OperationCanceledException)
        {
            LogHelper.Info(this, $"AI 分析 {alarm.SourcePath} 的 {alarm.AlarmCode}：{timeout} ms 内没等到回答");
            return HandleResult<AiAnswer>.Fail(ErrorCodes.AiTimeout, timeout.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception exception)
        {
            LogHelper.Info(this, $"AI 分析 {alarm.SourcePath} 的 {alarm.AlarmCode} 没做成：{exception.Message}");
            return HandleResult<AiAnswer>.Fail(ErrorCodes.AiFailed, exception.Message);
        }
        finally
        {
            if (entered)
            {
                slots.Release();
            }
        }
    }

    /// <summary>
    /// 当前报警里找这一条（来源不分大小写）；没有为 null
    /// </summary>
    private AlarmItem? FindAlarm(string sourcePath, string alarmCode)
    {
        var alarms = _alarms;
        if (alarms is null)
        {
            return null;
        }

        string source = sourcePath.Trim();
        string code = alarmCode.Trim();
        return alarms.ActiveAlarms.FirstOrDefault(item =>
            string.Equals(item.SourcePath, source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.AlarmCode, code, StringComparison.Ordinal));
    }

    /// <summary>
    /// 给模型的规矩：只按给的材料说，说不准就直说，只给建议
    /// </summary>
    private static string Instructions(string language)
    {
        var text = new StringBuilder();
        text.AppendLine("你是半导体设备的报警分析助手，帮现场工程师判断一条报警是怎么回事。");
        text.AppendLine("只根据给你的报警说明、信号和日志分析，不要编造没给出的数据；看不出来就直说看不出来，并说还要看什么。");
        text.AppendLine("按下面三段回答，每段几条短句：");
        text.AppendLine("1. 最可能的原因：最多 3 条，可能性大的在前，每条说依据（哪个信号怎么变的、哪条日志）。");
        text.AppendLine("2. 先查哪里：按先后，具体到部件、信号、接线、气路。");
        text.AppendLine("3. 处理建议：简短；不要建议跳过联锁、屏蔽报警或改安全设置。");
        if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            text.Append("Answer in English.");
        }
        else
        {
            text.Append("用简体中文回答。");
        }

        return text.ToString();
    }

    #endregion

    #region 整理材料

    /// <summary>
    /// 交给模型的材料：正文，带了几条日志、哪些信号
    /// </summary>
    private sealed record Material(string Prompt, int LogCount, IReadOnlyList<string> Signals);

    /// <summary>
    /// 整理材料：报警本身、报警前后相关的信号怎么变的、最近的告警和错误日志
    /// </summary>
    private Material Collect(AlarmItem alarm, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        AppendAlarm(text, alarm);
        var signals = AppendSignals(text, alarm, cancellationToken);
        int logs = AppendLogs(text);
        return new Material(text.ToString(), logs, signals);
    }

    private static void AppendAlarm(StringBuilder text, AlarmItem alarm)
    {
        text.AppendLine("【报警】");
        text.AppendLine($"来源：{alarm.SourcePath}");
        text.AppendLine($"报警：{alarm.AlarmText}（{alarm.AlarmCode}）");
        text.AppendLine($"等级：{alarm.Level}；类别：{alarm.Category}");
        text.AppendLine($"报出时间：{alarm.RaisedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(alarm.Description))
        {
            text.AppendLine($"说明：{alarm.Description}");
        }

        if (!string.IsNullOrWhiteSpace(alarm.Solution))
        {
            text.AppendLine($"处理办法：{alarm.Solution}");
        }

        text.AppendLine();
    }

    /// <summary>
    /// 报警来源下面的信号
    /// </summary>
    /// <param name="text"></param>
    /// <param name="alarm"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private IReadOnlyList<string> AppendSignals(StringBuilder text, AlarmItem alarm, CancellationToken cancellationToken)
    {
        var chart = _chart;
        int count = SignalCount;
        if (chart is null || count <= 0)
        {
            return [];
        }

        string prefix = alarm.SourcePath.Replace(".", DataSignal.Separator);
        var related = chart.Signals
            .Where(signal => string.Equals(signal.Name, prefix, StringComparison.OrdinalIgnoreCase)
                || signal.Name.StartsWith(prefix + DataSignal.Separator, StringComparison.OrdinalIgnoreCase))
            .Take(count)
            .ToList();
        if (related.Count == 0)
        {
            return [];
        }

        var raisedAt = alarm.RaisedAt.LocalDateTime;
        var window = TimeSpan.FromSeconds(SignalWindowSeconds);
        var start = raisedAt - window;
        var end = raisedAt + window;
        var now = DateTime.Now;
        if (end > now)
        {
            end = now;
        }

        var result = DataQueryResult.Empty;
        string note = string.Empty;
        if (end > start)
        {
            try
            {
                result = chart.Query(related.Select(signal => signal.Name).ToList(), start, end, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                note = $"（历史曲线查不到：{exception.Message}）";
            }
        }

        text.AppendLine($"【报警前后的信号】（{start.ToString(ClockFormat, CultureInfo.InvariantCulture)} ～ {end.ToString(ClockFormat, CultureInfo.InvariantCulture)}，每 {chart.SampleIntervalMs} ms 采一次；数字量 1 = 通 / 触发）{note}");
        long raisedMs = alarm.RaisedAt.ToUnixTimeMilliseconds();
        foreach (var signal in related)
        {
            var series = result.Series.FirstOrDefault(item => string.Equals(item.Name, signal.Name, StringComparison.Ordinal));
            text.AppendLine(DescribeSignal(signal, series, result.Times, raisedMs));
        }

        text.AppendLine();
        return related.Select(signal => signal.Name).ToList();
    }

    /// <summary>
    /// 一个信号写成一行：数字量列每次变化，模拟量写最小、最大和报警那一刻的值；最后都写现在的值
    /// </summary>
    private static string DescribeSignal(DataSignal signal, DataQuerySeries? series, long[] times, long raisedMs)
    {
        var line = new StringBuilder();
        line.Append(signal.Name).Append('（').Append(signal.Source);
        if (signal.Unit.Length > 0)
        {
            line.Append('，').Append(signal.Unit);
        }

        if (signal.Description.Length > 0)
        {
            line.Append('，').Append(signal.Description);
        }

        line.Append("）：");
        string history;
        if (series is null)
        {
            history = "这段时间没有记录";
        }
        else if (signal.IsDigital)
        {
            history = DigitalHistory(series.Values, times);
        }
        else
        {
            history = AnalogHistory(series.Values, times, raisedMs);
        }

        line.Append(history).Append("；现在 ").Append(ValueText(signal.Read(), signal.IsDigital));
        return line.ToString();
    }

    /// <summary>
    /// 数字量：第一个记录的值，后面每次变化的时刻和新值（最多 MaxChangesPerSignal 次）
    /// </summary>
    private static string DigitalHistory(float?[] values, long[] times)
    {
        var parts = new List<string>();
        float? last = null;
        int changes = 0;
        for (int i = 0; i < values.Length && i < times.Length; i++)
        {
            var value = values[i];
            if (value is null)
            {
                continue;
            }

            if (last is null)
            {
                parts.Add($"{Clock(times[i])} 起是 {ValueText(value, true)}");
                last = value;
                continue;
            }

            if (ValueText(value, true) == ValueText(last, true))
            {
                continue;
            }

            if (changes >= MaxChangesPerSignal)
            {
                parts.Add("……");
                break;
            }

            parts.Add($"{Clock(times[i])} 变成 {ValueText(value, true)}");
            last = value;
            changes++;
        }

        if (parts.Count == 0)
        {
            return "这段时间没有记录";
        }

        return string.Join("，", parts);
    }

    /// <summary>
    /// 模拟量：这段时间的最小、最大，报警那一刻（报警前最后一个记录）的值
    /// </summary>
    private static string AnalogHistory(float?[] values, long[] times, long raisedMs)
    {
        float? min = null;
        float? max = null;
        float? atAlarm = null;
        for (int i = 0; i < values.Length && i < times.Length; i++)
        {
            var value = values[i];
            if (value is null)
            {
                continue;
            }

            if (min is null || value < min)
            {
                min = value;
            }

            if (max is null || value > max)
            {
                max = value;
            }

            if (times[i] <= raisedMs)
            {
                atAlarm = value;
            }
        }

        if (min is null)
        {
            return "这段时间没有记录";
        }

        return $"最小 {ValueText(min, false)}，最大 {ValueText(max, false)}，报警时 {ValueText(atAlarm, false)}";
    }

    /// <summary>
    /// 最近的告警、错误日志（最旧的在前，最多 EC LogCount 条）；返回带了几条
    /// </summary>
    private int AppendLogs(StringBuilder text)
    {
        var logs = _logs;
        int count = LogCount;
        if (logs is null || count <= 0)
        {
            return 0;
        }

        var recent = logs.Recent();
        var picked = recent.Skip(Math.Max(0, recent.Count - count)).ToList();
        if (picked.Count == 0)
        {
            return 0;
        }

        text.AppendLine("【最近的告警、错误日志】（最旧的在前）");
        foreach (var log in picked)
        {
            text.AppendLine($"{log.Time.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {log.Level} {log.Module}：{log.Message}");
        }

        text.AppendLine();
        return picked.Count;
    }

    /// <summary>
    /// 数据曲线的时刻（UTC 毫秒）写成本机时间
    /// </summary>
    private static string Clock(long utcMilliseconds)
    {
        return DateTimeHelper.FromUnixMilliseconds(utcMilliseconds).ToString(ClockFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 值的写法：数字量 0 / 1，模拟量最多三位小数，读不到写"读不到"
    /// </summary>
    private static string ValueText(double? value, bool digital)
    {
        if (value is null)
        {
            return "读不到";
        }

        if (digital)
        {
            return value.Value >= 0.5 ? "1" : "0";
        }

        return ValueHelper.ToText(value.Value);
    }

    #endregion
}
