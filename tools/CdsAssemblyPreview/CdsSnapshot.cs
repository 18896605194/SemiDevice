using xyz.Shared.Dtos;

namespace CdsAssemblyPreview;

/// <summary>工具的只读装机清单，布局不决定设备数量。</summary>
public sealed record CdsSnapshot(string ComponentRoot, IReadOnlyList<CdsTankSnapshot> Tanks, IReadOnlyList<CdsDeviceSnapshot>? ExtraDevices = null)
{
    public IEnumerable<CdsDeviceSnapshot> Devices => ExtraDevices ?? [];
    public IEnumerable<string> ValvePaths => Tanks.SelectMany(t => t.Routes).SelectMany(r => r.ValvePaths)
        .Concat(Devices.Where(d => d.Kind == "Valve").Select(d => d.Path)).Distinct(StringComparer.Ordinal);
    public IEnumerable<(string Path, DiSensorDto Signal)> Sensors => Tanks.SelectMany(t => CdsSensors.For(t).Select(s => (t.Data.Path + "." + s.Name, s)))
        .Concat(Devices.Where(d => d.Signal is not null).Select(d => (d.Path, d.Signal!))).DistinctBy(s => s.Item1);
}

public sealed record CdsDeviceSnapshot(string Kind, string Path, string Name, DiSensorDto? Signal = null, FluidHardwareKind? VisualKind = null);

public sealed record CdsTankSnapshot(TankDto Data, IReadOnlyList<CdsRouteDefinition> Routes);

/// <param name="Kind">fill / drain / supply，与 ToolBuilder 布局中的管路用途对应。</param>
/// <param name="ValvePaths">已安装的阀门完整路径。这里只显示配置，不推断阀门反馈。</param>
public sealed record CdsRouteDefinition(string Kind, IReadOnlyList<string> ValvePaths);

public static class CdsSensors
{
    public static IEnumerable<DiSensorDto> For(CdsTankSnapshot tank) => new[] { tank.Data.Overfill, tank.Data.HH, tank.Data.H, tank.Data.L, tank.Data.LL }
        .Where(sensor => sensor is not null).Select(sensor => sensor!);
}
