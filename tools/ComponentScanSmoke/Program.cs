using System.Collections.Concurrent;
using System.Xml.Serialization;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Configs.Models;

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
var roots = ComponentLoader.Load(config.Modules);
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
Console.WriteLine("PASS: configured root scans, case-insensitive SC, default/disabled gating, groups, child sharing and concurrent duplicate starts");

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
