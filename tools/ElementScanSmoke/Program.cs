using System.Collections.Concurrent;
using System.Xml.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using xyz.Common.Log;
using xyz.Elements;
using xyz.Elements.Attributes;
using xyz.Configs.Models;
using xyz.Smoke;
using xyz.Tools;

SmokeIoc.Reset();

const string xml = """
    <ArrayOfSetting>
      <Setting Name="Running" Type="ScanProbe">
        <Value Name="isRUn" Value="TrUe" />
        <Setting Name="Child" Type="ScanProbe">
          <Value Name="IsRun" Value="true" />
        </Setting>
        <Setting Name="SharedChild" Type="ScanProbe" />
      </Setting>
      <Setting Name="DefaultOff" Type="ScanProbe" />
      <Setting Name="ExplicitOff" Type="ScanProbe">
        <Value Name="ISRUN" Value="false" />
      </Setting>
      <Setting Name="Group">
        <Setting Name="GroupedRoot" Type="ScanProbe">
          <Value Name="isrun" Value="true" />
        </Setting>
      </Setting>
      <Setting Name="Disabled" Type="DisabledScanProbe">
        <Value Name="IsRun" Value="true" />
      </Setting>
    </ArrayOfSetting>
    """;

var serializer = new XmlSerializer(typeof(MachineSettings));
using var reader = new StringReader(xml);
var config = (MachineSettings)serializer.Deserialize(reader)!;
var roots = SmokeIoc.Load(config.Modules);
var running = (ScanProbe)roots.Single(root => root.Name == "Running");
var child = (ScanProbe)running.Children.Single(element => element.Name == "Child");
var sharedChild = (ScanProbe)running.Children.Single(element => element.Name == "SharedChild");
var grouped = (ScanProbe)roots.Single(root => root.Name == "GroupedRoot");
var idle = roots.OfType<ScanProbe>().Where(root => root != running && root != grouped).ToList();

Check(running.IsRun && child.IsRun && grouped.IsRun && !sharedChild.IsRun,
    "machine.xml property names and boolean values are case insensitive; default is false");
Check(grouped.FullPath == "Group.GroupedRoot", "untyped groups preserve root scan eligibility");
Check(roots.OfType<ScanProbe>().All(root => root.Threads.IsEmpty), "assembly alone starts no scan threads");

ElementLoader.StartScanning(roots);
Check(SpinWait.SpinUntil(() => running.Count >= 3 && child.Count >= 3 && sharedChild.Count >= 3 && grouped.Count >= 3, 3000),
    "enabled roots and their children must scan");
Check(idle.All(root => root.Count == 0), "omitted, false and disabled roots must not scan");
Check(child.Threads.Keys.SequenceEqual(running.Threads.Keys)
    && sharedChild.Threads.Keys.SequenceEqual(running.Threads.Keys), "children share the parent thread regardless of IsRun");
Check(!grouped.Threads.Keys.Intersect(running.Threads.Keys).Any(), "separate roots have separate scan threads");

// 并发重复调用统一入口和直接 Start，都不能产生第二条扫描线程。
Parallel.For(0, 20, _ =>
{
    ElementLoader.StartScanning(roots);
    running.Start();
});
Thread.Sleep(200);
Check(running.Threads.Count == 1 && grouped.Threads.Count == 1 && child.Threads.Count == 1,
    "repeat starts must be idempotent");

// 扫描周期 10ms（2026-10-22 从 50ms 改成 20ms、又改成 10ms）：1 秒里至少扫 55 拍（20ms 时最多 50 拍），给计时精度和机器忙留余量
int scansBefore = running.Count;
Thread.Sleep(1000);
int scansInOneSecond = running.Count - scansBefore;
Check(scansInOneSecond >= 55, $"roots scan about every 10 ms: {scansInOneSecond} scans in one second");

// 退出（2026-10-27）：StopAll 先停根元件的扫描、等手上这一拍扫完，再倒着收尾；收尾的时候扫描已经停了，之后也不再扫
var slow = new SlowScanProbe();
slow.Start();
Check(SpinWait.SpinUntil(() => slow.InScan, 3000), "a slow root is in the middle of a scan");
ElementLoader.StopAll([.. roots, slow]);
Check(!slow.InScan, "StopAll waits for the scan in progress to finish");
var countsAfterStop = (running.Count, child.Count, grouped.Count, slow.Count);
Thread.Sleep(100);
Check((running.Count, child.Count, grouped.Count, slow.Count) == countsAfterStop, "no root or child scans after StopAll");
Check(new ScanProbe[] { running, child, sharedChild, grouped, slow }.All(probe => probe.Stopped && !probe.ScannedWhileStopping),
    "OnStopping runs once scanning has stopped");

// 在自己的扫描里停自己：不等自己（等就卡住了），这一拍扫完循环自己出来
var selfStop = new SelfStoppingScanProbe();
selfStop.Start();
Check(SpinWait.SpinUntil(() => selfStop.Returned, 1000), "a root stopping its own scan from inside a scan returns at once");
Thread.Sleep(100);
Check(selfStop.Count == 1, "after stopping itself the root scans no more");

// 开机各阶段（元件自己的开机准备、收尾都挂在这几个钩子上）：元件初始化按 InitOrder、再按 machine.xml 先后，子元件跟着父元件；
// Bind、OnStarted、开机推送 PublishOnStartup 按 machine.xml 先后（先父后子），推送一个出错不耽误后面的；退出 OnStopping 倒着来，一个出错不耽误后面的；停用的连同下面的都不参与。
// 进容器（宿主开机装配那一步，ElementLoader.Register）：每个元件只按打了 [Ioc] 的接口登记，没打的接口实现了也进不去（模块内部的部件就靠这个挡在容器外），
// 取一类全部按 machine.xml 先后、停用的连同下面跳过，同一接口登记了好几个、取一个的拿最后登记的，没人实现的取不到（null）；
// 装好的树本身也登记（宿主开机、收尾按它走），不进容器的部件用 ElementLoader.FindAll 在树上找。
const string lifecycleXml = """
    <ArrayOfSetting>
      <Setting Name="Late" Type="LifecycleProbe">
        <Setting Name="LateChild" Type="LifecycleProbe" />
      </Setting>
      <Setting Name="Early" Type="LifecycleProbe" InitOrder="5" />
      <Setting Name="Off" Type="DisabledLifecycleProbe">
        <Setting Name="OffChild" Type="LifecycleProbe" />
      </Setting>
      <Setting Name="Faulty" Type="FaultyLifecycleProbe" />
    </ArrayOfSetting>
    """;
using var lifecycleReader = new StringReader(lifecycleXml);
var lifecycleRoots = ElementLoader.Load(((MachineSettings)serializer.Deserialize(lifecycleReader)!).Modules);
var services = new ServiceCollection();
ElementLoader.Register(services, lifecycleRoots);
IocHelper.ServiceProvider = services.BuildServiceProvider();
Check(ReferenceEquals(IocHelper.GetRequiredService<IReadOnlyList<ElementBase>>(), lifecycleRoots), "the assembled tree itself goes into the container");
Check(string.Join(",", IocHelper.GetServices<ILifecycleProbe>().Select(probe => probe.Name)) == "Late,LateChild,Early,Faulty",
    "every element goes in by its xyz interfaces in machine.xml order; a disabled node is skipped with everything under it");
Check(IocHelper.GetService<ILifecycleProbe>()?.Name == "Faulty" && IocHelper.GetService<INotInstalledProbe>() is null,
    "asking for one gives the last registered; an interface nobody implements is not installed (null)");
Check(IocHelper.GetService<IUnmarkedProbe>() is null && ElementLoader.FindAll<IUnmarkedProbe>().Count == 4,
    "an interface without [Ioc] stays out of the container even though elements implement it; FindAll still finds them on the tree");
ElementLoader.InitAll(lifecycleRoots);
ElementLoader.BindAll(lifecycleRoots);
ElementLoader.NotifyStartedAll(lifecycleRoots);
ElementLoader.PublishStartupAll(lifecycleRoots);
ElementLoader.StopAll(lifecycleRoots);
Check(LifecycleProbe.Calls("Init") == "Early,Late,LateChild,Faulty", "InitAll goes by InitOrder, then machine.xml order; children follow their parent");
Check(LifecycleProbe.Calls("Bind") == "Late,LateChild,Early,Faulty" && LifecycleProbe.Calls("Started") == "Late,LateChild,Early,Faulty",
    "Bind and OnStarted run in machine.xml order, parent before child");
Check(LifecycleProbe.Calls("Publish") == "Late,LateChild,Early,Faulty",
    "PublishStartupAll runs PublishOnStartup in machine.xml order, parent before child, and an element that throws does not stop startup");
Check(LifecycleProbe.Calls("Stopping") == "Faulty,Early,LateChild,Late", "StopAll runs in reverse and keeps going after an element throws");

// machine.xml 里写错的参数名（元件上没有这个 [Machine] 属性）：照常装上，那一项还是默认值，记一条错误日志（开机读配置的事记在 Host 下），写清哪个节点、哪个名字
var logs = new ConcurrentQueue<LogItem>();
LogQueue.Start(logs.Enqueue);
const string typoXml = """
    <ArrayOfSetting>
      <Setting Name="TypoGroup">
        <Setting Name="Typo" Type="ScanProbe">
          <Value Name="IsRunn" Value="true" />
        </Setting>
      </Setting>
    </ArrayOfSetting>
    """;
using var typoReader = new StringReader(typoXml);
var typo = (ScanProbe)ElementLoader.Load(((MachineSettings)serializer.Deserialize(typoReader)!).Modules).Single();
Check(!typo.IsRun, "a misspelled machine.xml name still loads and leaves that setting at its default");
Check(SpinWait.SpinUntil(() => logs.Any(item => item.Level == LogLevel.Error && item.Module == LogHelper.HostModule
        && item.Message.Contains("TypoGroup.Typo") && item.Message.Contains("IsRunn")), 3000),
    "a misspelled machine.xml name is logged as an error naming the node path and the name");

Console.WriteLine("PASS: configured root scans, case-insensitive machine.xml, default/disabled gating, groups, child sharing and concurrent duplicate starts, a 10 ms scan period; on exit scans stop first (waiting for the scan in progress, none during or after OnStopping, a root stopping itself from its own scan does not hang); lifecycle stages (init by InitOrder, bind, started and publish-on-startup in machine.xml order, stopping in reverse past a failure), elements into the container by interface; a misspelled machine.xml name logged as an error and left at its default");

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

[Element]
public class ScanProbe : ElementBase
{
    public ConcurrentDictionary<int, byte> Threads { get; } = new();
    private int _count;
    public int Count => Volatile.Read(ref _count);
    private volatile bool _stopped;
    public bool Stopped => _stopped;

    /// <summary>
    /// 收尾以后又开始扫了一拍
    /// </summary>
    public bool ScannedWhileStopping { get; private set; }

    protected override void OnScan()
    {
        if (_stopped)
        {
            ScannedWhileStopping = true;
        }

        Threads.TryAdd(Environment.CurrentManagedThreadId, 0);
        base.OnScan();
        Interlocked.Increment(ref _count);
    }

    public override void OnStopping()
    {
        _stopped = true;
    }
}

// 一拍扫 150ms：停扫描要等它扫完这一拍
[Element]
public sealed class SlowScanProbe : ScanProbe
{
    private volatile bool _inScan;
    public bool InScan => _inScan;

    protected override void OnScan()
    {
        _inScan = true;
        Thread.Sleep(150);
        base.OnScan();
        _inScan = false;
    }
}

// 在自己的扫描里调 StopScan 停自己
[Element]
public sealed class SelfStoppingScanProbe : ScanProbe
{
    private volatile bool _returned;
    public bool Returned => _returned;

    protected override void OnScan()
    {
        base.OnScan();
        StopScan();
        _returned = true;
    }
}

[Element]
public sealed class DisabledScanProbe : ScanProbe
{
    public DisabledScanProbe()
    {
        typeof(ElementBase).GetProperty(nameof(IsEnabled))!.SetValue(this, false);
    }
}

// 记下每个阶段走到了谁
[Element]
public class LifecycleProbe : ElementBase, ILifecycleProbe, IUnmarkedProbe
{
    private static readonly ConcurrentQueue<(string Stage, string Name)> Log = new();

    public static string Calls(string stage)
    {
        return string.Join(",", Log.Where(call => call.Stage == stage).Select(call => call.Name));
    }

    public override bool InitElement()
    {
        Log.Enqueue(("Init", Name));
        return base.InitElement();
    }

    public override void Bind()
    {
        Log.Enqueue(("Bind", Name));
    }

    public override void OnStarted()
    {
        Log.Enqueue(("Started", Name));
    }

    public override void PublishOnStartup()
    {
        Log.Enqueue(("Publish", Name));
    }

    public override void OnStopping()
    {
        Log.Enqueue(("Stopping", Name));
    }
}

[Element]
public sealed class DisabledLifecycleProbe : LifecycleProbe
{
    public DisabledLifecycleProbe()
    {
        typeof(ElementBase).GetProperty(nameof(IsEnabled))!.SetValue(this, false);
    }
}

// 开机推送、收尾出错：开机照样往下走，后面的照样收
[Element]
public sealed class FaultyLifecycleProbe : LifecycleProbe
{
    public override void PublishOnStartup()
    {
        base.PublishOnStartup();
        throw new InvalidOperationException("probe publish failure");
    }

    public override void OnStopping()
    {
        base.OnStopping();
        throw new InvalidOperationException("probe stopping failure");
    }
}

// 探针的接口打上 [Ioc]：装配器只按打了标记的接口登记
namespace xyz.Smoke
{
    [Ioc]
    public interface ILifecycleProbe
    {
        string Name { get; }
    }

    // 谁都没实现：取不到
    [Ioc]
    public interface INotInstalledProbe
    {
    }

    // 没打标记：元件实现了也不进容器
    public interface IUnmarkedProbe
    {
    }
}
