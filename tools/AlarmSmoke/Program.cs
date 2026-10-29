using xyz.Elements;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Configs.Models;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Tools;

// 报警冒烟：报出来那一下按策略处理（定归属、建上下文、按等级挑策略），以及通知走自己的消费线程。
// 不连设备、不写配置文件；入库和推界面在 WaferLedgerSmoke 里查。
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

#region 搭树：报警元件 + 两层嵌套的模块 + 不在模块下的元件

var alarms = Load("RecordingStrategy", "RecordingStrategy", "RecordingStrategy", "");

// 嵌套：Chamber 自己也是模块，报警来自 Chamber.Chamber1 时要算到里面那个
var outer = NewModule("Chamber", "Chamber");
var inner = NewModule("Chamber1", "Chamber.Chamber1");

// 安全信号不在任何模块下：归属算整机（null）
var safety = NewSource("Emo", "Safety.Emo");

SmokeIoc.Tree(alarms, outer, inner, safety);
alarms.Bind();

var changed = new List<AlarmItem>();
alarms.AlarmChanged += item =>
{
    lock (changed)
    {
        changed.Add(item);
    }
};

#endregion

#region 按策略处理：报出来那一下调策略，清除不调

inner.Raise(SmokeModule.HeavyAlarmCode);
Check(alarms.ActiveAlarms.Count == 1, $"报警应报出来，实际 {alarms.ActiveAlarms.Count} 条");

var calls = RecordingStrategy.Snapshot();
Check(calls.Count == 1, $"报出来那一下应调一次策略，实际 {calls.Count} 次");
Check(calls[0].Alarm.SourcePath == "Chamber.Chamber1"
      && calls[0].Alarm.AlarmCode == SmokeModule.HeavyAlarmCode,
    "策略拿到的应该是刚报出来的那条报警");
Check(calls[0].Alarm.Level == AlarmLevel.Heavy, "策略拿到的报警应带等级");

// 归属：Chamber 和 Chamber1 都前缀匹配，要的是路径最长的那个
Check(ReferenceEquals(calls[0].Module, inner),
    $"嵌套时归属应算到最近的模块，实际 {calls[0].Module?.Name}");

// 清除走人工复位，不触发策略
RecordingStrategy.Clear();
Check(alarms.Reset("Chamber.Chamber1"), "按来源复位应找到模块");
Check(alarms.ActiveAlarms.Count == 0, "复位后应移出报警列表");
Check(RecordingStrategy.Snapshot().Count == 0, "报警清除不该触发策略");

#endregion

#region 归属：不在任何模块下的算整机

RecordingStrategy.Clear();
safety.Raise(SmokeSource.AlarmCode);
calls = RecordingStrategy.Snapshot();
Check(calls.Count == 1, $"不在模块下的报警也要调策略，实际 {calls.Count} 次");
Check(calls[0].Module is null, "不在任何模块下的报警，归属应为 null（算整机）");
alarms.Reset("Safety.Emo");

#endregion

#region 按等级挑策略：配了的调，没配的不调也不报错

var byLevel = Load("", "", "RecordingStrategy", "");
SmokeIoc.Tree(byLevel, outer, inner, safety);
byLevel.Bind();
RecordingStrategy.Clear();

inner.Raise(SmokeModule.WarnAlarmCode);
Check(byLevel.ActiveAlarms.Count == 1, "警告也要报出来");
Check(RecordingStrategy.Snapshot().Count == 0, "这个等级没配，走平台默认的，不该调到别的等级配的策略");

inner.Raise(SmokeModule.HeavyAlarmCode);
calls = RecordingStrategy.Snapshot();
Check(calls.Count == 1, $"配了策略的等级才调，实际 {calls.Count} 次");
Check(calls[0].Alarm.Level == AlarmLevel.Heavy, "调到的应该是这个等级配的策略");
inner.Reset();

#endregion

#region 策略出错不挡报警本身

var faulty = Load("", "", "ThrowingStrategy", "");
SmokeIoc.Tree(faulty, outer, inner, safety);
faulty.Bind();

var faultyChanged = new List<AlarmItem>();
faulty.AlarmChanged += item =>
{
    lock (faultyChanged)
    {
        faultyChanged.Add(item);
    }
};

inner.Raise(SmokeModule.HeavyAlarmCode);
Check(faulty.ActiveAlarms.Count == 1, "策略抛异常，报警本身照样记下来");
Check(WaitFor(faultyChanged, 1, 2000) == 1, "策略抛异常，照样推给订阅者（入库、报 Host 不能被策略挡住）");
inner.Reset();

#endregion

#region 通知走自己的线程：报警的线程不等它，最终都要到

RecordingStrategy.Clear();
SmokeIoc.Tree(alarms, outer, inner, safety);
alarms.Bind();
lock (changed)
{
    changed.Clear();
}

for (int round = 0; round < 20; round++)
{
    inner.Raise(SmokeModule.HeavyAlarmCode);
    inner.Reset();
}

Check(WaitFor(changed, 40, 5000) == 40, "报出、清除各推一条，攒下来的通知最终都要发完");

#endregion

#region 一个都不配：四个等级都走平台默认的，报警照样报出来、照样推

var plain = Load("", "", "", "");
SmokeIoc.Tree(plain, outer, inner, safety);
plain.Bind();

var plainChanged = new List<AlarmItem>();
plain.AlarmChanged += item =>
{
    lock (plainChanged)
    {
        plainChanged.Add(item);
    }
};

inner.Raise(SmokeModule.HeavyAlarmCode);
Check(plain.ActiveAlarms.Count == 1, "一个策略都不配也要能报出来（平台默认策略什么都不做）");
Check(WaitFor(plainChanged, 1, 2000) == 1, "一个策略都不配，照样推给订阅者");
inner.Reset();

#endregion

#region 致命等级：只认平台那个的子类，机型继承它能生效

var fatal = Load("", "", "", "SmokeFatalStrategy");
SmokeIoc.Tree(fatal, outer, inner, safety);
fatal.Bind();

inner.Raise(SmokeModule.FatalAlarmCode);
Check(SmokeFatalStrategy.Calls() == 1, $"继承平台致命策略的机型策略应被调到，实际 {SmokeFatalStrategy.Calls()} 次");
inner.Reset();

// 不继承 FatalAlarmStrategy 的，哪怕实现了 IAlarmStrategy 也不许配在致命等级上
var badFatal = Load("", "", "", "RecordingStrategy");
SmokeIoc.Tree(badFatal, outer, inner, safety);
var fatalRejected = false;
try
{
    badFatal.Bind();
}
catch (InvalidOperationException)
{
    fatalRejected = true;
}

Check(fatalRejected, "致命等级配了不继承 FatalAlarmStrategy 的策略，开机就要抛");

#endregion

#region 策略名配错：开机就抛，不留到报警时才发现

var wrong = Load("", "", "根本没有这个策略", "");
SmokeIoc.Tree(wrong, outer, inner, safety);
var failed = false;
try
{
    wrong.Bind();
}
catch (InvalidOperationException)
{
    failed = true;
}

Check(failed, "machine.xml 配了不存在的策略类，开机就要抛");

#endregion

alarms.OnStopping();
Console.WriteLine($"PASS: {checks} alarm checks (strategy dispatch on raise, ownership by nearest module, "
    + "system-level alarms with no module, per-level strategy pick, the platform defaults when nothing is configured, "
    + "the fatal level only accepting FatalAlarmStrategy subclasses (a machine one overriding it gets called), "
    + "a throwing strategy not blocking the alarm, the notification consumer thread draining everything, "
    + "and a bad strategy name failing at startup).");

#region 冒烟用的小件

// 装一个报警元件，四个等级各配一个策略类名（空 = 这个等级不配）
static AlarmElement Load(string warn, string light, string heavy, string fatal)
{
    return SmokeIoc.Load([
        new ModuleConfig
        {
            Name = "Alarm",
            Type = typeof(AlarmElement).FullName,
            Values =
            [
                new ValueConfig { Name = "EnableHistory", Value = "False" },
                new ValueConfig { Name = "WarnStrategy", Value = warn },
                new ValueConfig { Name = "LightStrategy", Value = light },
                new ValueConfig { Name = "HeavyStrategy", Value = heavy },
                new ValueConfig { Name = "FatalStrategy", Value = fatal },
            ],
        },
    ]).OfType<AlarmElement>().Single();
}

// 假模块：名字和全路径照 machine.xml 装出来的样子设
static SmokeModule NewModule(string name, string fullPath)
{
    var module = new SmokeModule();
    SetPath(module, name, fullPath);
    return module;
}

// 不在模块下的报警来源（安全信号这类）
static SmokeSource NewSource(string name, string fullPath)
{
    var source = new SmokeSource();
    SetPath(source, name, fullPath);
    return source;
}

static void SetPath(ElementBase element, string name, string fullPath)
{
    typeof(ElementBase).GetProperty("Name")!.SetValue(element, name);
    typeof(ElementBase).GetProperty("FullPath")!.SetValue(element, fullPath);
}

// 通知是消费线程发的，等它追上来再查
static int WaitFor(List<AlarmItem> items, int expected, int timeoutMs)
{
    for (int waited = 0; waited < timeoutMs; waited += 10)
    {
        lock (items)
        {
            if (items.Count >= expected)
            {
                return items.Count;
            }
        }

        Thread.Sleep(10);
    }

    lock (items)
    {
        return items.Count;
    }
}

#endregion

/// <summary>
/// 冒烟用的假模块：能报警，也能当报警的归属
/// </summary>
sealed class SmokeModule : BaseModule
{
    public const string HeavyAlarmCode = nameof(SmokeHeavyAlarm);

    public const string WarnAlarmCode = nameof(SmokeWarnAlarm);

    public const string FatalAlarmCode = nameof(SmokeFatalAlarm);

    public override int State { get; protected set; } = ModuleState.Idle;

    [Alarm("假模块重报警", AlarmCategory.IrrecoverableError, AlarmLevel = AlarmLevel.Heavy,
        Description = "冒烟用", Solution = "冒烟用")]
    public string SmokeHeavyAlarm = HeavyAlarmCode;

    [Alarm("假模块警告", AlarmCategory.EquipmentStatusWarning, AlarmLevel = AlarmLevel.Warn,
        Description = "冒烟用", Solution = "冒烟用")]
    public string SmokeWarnAlarm = WarnAlarmCode;

    [Alarm("假模块致命报警", AlarmCategory.PersonalSafety, AlarmLevel = AlarmLevel.Fatal,
        Description = "冒烟用", Solution = "冒烟用")]
    public string SmokeFatalAlarm = FatalAlarmCode;

    /// <summary>
    /// 报警是 protected 的，开个口子给冒烟调
    /// </summary>
    public void Raise(string code)
    {
        RaiseAlarm(code);
    }
}

/// <summary>
/// 冒烟用的报警来源：不是模块，用来验归属算整机
/// </summary>
sealed class SmokeSource : ElementBase
{
    public const string AlarmCode = nameof(SmokeSafetyAlarm);

    [Alarm("假安全信号", AlarmCategory.PersonalSafety, AlarmLevel = AlarmLevel.Heavy,
        Description = "冒烟用", Solution = "冒烟用")]
    public string SmokeSafetyAlarm = AlarmCode;

    public void Raise(string code)
    {
        RaiseAlarm(code);
    }
}

/// <summary>
/// 冒烟用的假策略：把每次调用记下来
/// </summary>
sealed class RecordingStrategy : IAlarmStrategy
{
    private static readonly List<AlarmContext> Calls = [];

    public void OnRaised(AlarmContext context)
    {
        lock (Calls)
        {
            Calls.Add(context);
        }
    }

    public static List<AlarmContext> Snapshot()
    {
        lock (Calls)
        {
            return new List<AlarmContext>(Calls);
        }
    }

    public static void Clear()
    {
        lock (Calls)
        {
            Calls.Clear();
        }
    }
}

/// <summary>
/// 冒烟用的坏策略：验证策略出错不挡住报警本身
/// </summary>
sealed class ThrowingStrategy : IAlarmStrategy
{
    public void OnRaised(AlarmContext context)
    {
        throw new InvalidOperationException("冒烟故意让策略出错");
    }
}

/// <summary>
/// 冒烟用：机型继承平台的致命策略加自己的东西，致命等级只认这种
/// </summary>
sealed class SmokeFatalStrategy : FatalAlarmStrategy
{
    private static int _calls;

    public override void OnRaised(AlarmContext context)
    {
        base.OnRaised(context);
        Interlocked.Increment(ref _calls);
    }

    public static int Calls()
    {
        return Volatile.Read(ref _calls);
    }
}
