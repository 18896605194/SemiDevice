using System.Collections.Concurrent;
using System.Xml.Serialization;
using Microsoft.Extensions.DependencyInjection;
using xyz.Components;
using xyz.Components.Attributes;
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

var serializer = new XmlSerializer(typeof(ScConfig));
using var reader = new StringReader(xml);
var config = (ScConfig)serializer.Deserialize(reader)!;
var roots = SmokeIoc.Load(config.Modules);
var running = (ScanProbe)roots.Single(root => root.Name == "Running");
var child = (ScanProbe)running.Children.Single(component => component.Name == "Child");
var sharedChild = (ScanProbe)running.Children.Single(component => component.Name == "SharedChild");
var grouped = (ScanProbe)roots.Single(root => root.Name == "GroupedRoot");
var idle = roots.OfType<ScanProbe>().Where(root => root != running && root != grouped).ToList();

Check(running.IsRun && child.IsRun && grouped.IsRun && !sharedChild.IsRun,
    "SC property names and boolean values are case insensitive; default is false");
Check(grouped.FullPath == "Group.GroupedRoot", "untyped groups preserve root scan eligibility");
Check(roots.OfType<ScanProbe>().All(root => root.Threads.IsEmpty), "assembly alone starts no scan threads");

ComponentLoader.StartScanning(roots);
Check(SpinWait.SpinUntil(() => running.Count >= 3 && child.Count >= 3 && sharedChild.Count >= 3 && grouped.Count >= 3, 3000),
    "enabled roots and their children must scan");
Check(idle.All(root => root.Count == 0), "omitted, false and disabled roots must not scan");
Check(child.Threads.Keys.SequenceEqual(running.Threads.Keys)
    && sharedChild.Threads.Keys.SequenceEqual(running.Threads.Keys), "children share the parent thread regardless of IsRun");
Check(!grouped.Threads.Keys.Intersect(running.Threads.Keys).Any(), "separate roots have separate scan threads");

// 并发重复调用统一入口和直接 Start，都不能产生第二条扫描线程。
Parallel.For(0, 20, _ =>
{
    ComponentLoader.StartScanning(roots);
    running.Start();
});
Thread.Sleep(200);
Check(running.Threads.Count == 1 && grouped.Threads.Count == 1 && child.Threads.Count == 1,
    "repeat starts must be idempotent");

// 开机各阶段（组件自己的开机准备、收尾都挂在这几个钩子上）：组件初始化按 InitOrder、再按 sc 先后，子组件跟着父组件；
// Bind、OnStarted 按 sc 先后（先父后子）；退出 OnStopping 倒着来，一个出错不耽误后面的；停用的连同下面的都不参与。
// 进容器（宿主开机装配那一步，ComponentLoader.Register）：每个组件按它实现的 xyz 接口登记，取一类全部按 sc 先后、停用的连同下面跳过，
// 同一接口登记了好几个、取一个的拿最后登记的，没人实现的取不到（null）；装好的树本身也登记（宿主开机、收尾按它走）。
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
var lifecycleRoots = ComponentLoader.Load(((ScConfig)serializer.Deserialize(lifecycleReader)!).Modules);
var services = new ServiceCollection();
ComponentLoader.Register(services, lifecycleRoots);
IocHelper.ServiceProvider = services.BuildServiceProvider();
Check(ReferenceEquals(IocHelper.GetRequiredService<IReadOnlyList<ComponentBase>>(), lifecycleRoots), "the assembled tree itself goes into the container");
Check(string.Join(",", IocHelper.GetServices<ILifecycleProbe>().Select(probe => probe.Name)) == "Late,LateChild,Early,Faulty",
    "every component goes in by its xyz interfaces in sc order; a disabled node is skipped with everything under it");
Check(IocHelper.GetService<ILifecycleProbe>()?.Name == "Faulty" && IocHelper.GetService<INotInstalledProbe>() is null,
    "asking for one gives the last registered; an interface nobody implements is not installed (null)");
ComponentLoader.InitAll(lifecycleRoots);
ComponentLoader.BindAll(lifecycleRoots);
ComponentLoader.StartedAll(lifecycleRoots);
ComponentLoader.StopAll(lifecycleRoots);
Check(LifecycleProbe.Calls("Init") == "Early,Late,LateChild,Faulty", "InitAll goes by InitOrder, then sc order; children follow their parent");
Check(LifecycleProbe.Calls("Bind") == "Late,LateChild,Early,Faulty" && LifecycleProbe.Calls("Started") == "Late,LateChild,Early,Faulty",
    "Bind and OnStarted run in sc order, parent before child");
Check(LifecycleProbe.Calls("Stopping") == "Faulty,Early,LateChild,Late", "StopAll runs in reverse and keeps going after a component throws");

Console.WriteLine("PASS: configured root scans, case-insensitive SC, default/disabled gating, groups, child sharing and concurrent duplicate starts; lifecycle stages (init by InitOrder, bind and started in sc order, stopping in reverse past a failure), components into the container by interface");

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

[Component]
public class ScanProbe : ComponentBase
{
    public ConcurrentDictionary<int, byte> Threads { get; } = new();
    private int _count;
    public int Count => Volatile.Read(ref _count);

    protected override void OnScan()
    {
        Threads.TryAdd(Environment.CurrentManagedThreadId, 0);
        base.OnScan();
        Interlocked.Increment(ref _count);
    }
}

[Component]
public sealed class DisabledScanProbe : ScanProbe
{
    public DisabledScanProbe()
    {
        typeof(ComponentBase).GetProperty(nameof(IsEnabled))!.SetValue(this, false);
    }
}

// 记下每个阶段走到了谁
[Component]
public class LifecycleProbe : ComponentBase, ILifecycleProbe
{
    private static readonly ConcurrentQueue<(string Stage, string Name)> Log = new();

    public static string Calls(string stage)
    {
        return string.Join(",", Log.Where(call => call.Stage == stage).Select(call => call.Name));
    }

    public override bool InitComponent()
    {
        Log.Enqueue(("Init", Name));
        return base.InitComponent();
    }

    public override void Bind()
    {
        Log.Enqueue(("Bind", Name));
    }

    public override void OnStarted()
    {
        Log.Enqueue(("Started", Name));
    }

    public override void OnStopping()
    {
        Log.Enqueue(("Stopping", Name));
    }
}

[Component]
public sealed class DisabledLifecycleProbe : LifecycleProbe
{
    public DisabledLifecycleProbe()
    {
        typeof(ComponentBase).GetProperty(nameof(IsEnabled))!.SetValue(this, false);
    }
}

// 收尾出错：后面的照样收
[Component]
public sealed class FaultyLifecycleProbe : LifecycleProbe
{
    public override void OnStopping()
    {
        base.OnStopping();
        throw new InvalidOperationException("probe stopping failure");
    }
}

// 探针的接口放在 xyz 开头的命名空间里：装配器只按 xyz 的接口登记
namespace xyz.Smoke
{
    public interface ILifecycleProbe
    {
        string Name { get; }
    }

    // 谁都没实现：取不到
    public interface INotInstalledProbe
    {
    }
}
