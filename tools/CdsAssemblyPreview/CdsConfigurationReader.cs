using System.Xml.Linq;
using ToolBuilder.Core;
using xyz.Shared.Dtos;

namespace CdsAssemblyPreview;

/// <summary>读取当前内存中的装机节点；只解析元数据，不实例化任何后台组件。</summary>
public static class CdsConfigurationReader
{
    public static string? Role(string type, ElementSpec? spec = null)
    {
        bool Contract(string name) => spec?.Interfaces?.Contains("xyz.Elements.Interfaces.I" + name) == true;
        string full = type.Split(',')[0].Trim();
        bool Platform(string name) => full == "xyz.Elements.Elements." + name + "Element";
        if (Contract("Tank") || Platform("Tank")) return "Tank";
        if (Contract("ValveGroup") || Platform("ValveGroup")) return "ValveGroup";
        if (Contract("Valve") || Platform("Valve")) return "Valve";
        if (Contract("DiSensor") || Platform("DiSensor")) return "Sensor";
        if (Contract("AiSensor") || Platform("AiSensor")) return "Analog";
        if (spec?.BaseTypes?.Contains("xyz.Modules.BaseCdsModule") == true || spec?.Interfaces?.Contains("xyz.Modules.ISupplyModule") == true) return "Module";
        foreach (var kind in Enum.GetValues<FluidHardwareKind>().Where(k => k is not (FluidHardwareKind.Tank or FluidHardwareKind.OnOffValve or FluidHardwareKind.LeakSensor)))
            if (Contract(kind.ToString()) || Platform(kind.ToString()) || spec?.BaseTypes?.Contains("xyz.Elements.Elements." + kind + "Element") == true) return "Hardware:" + kind;
        if (Contract("Pump") || Platform("Pump")) return "Hardware:CentrifugalPump";
        if (Contract("Heater") || Platform("Heater")) return "Hardware:InlineHeater";
        return null;
    }

    public static CdsSnapshot Read(XElement module, Func<string, string?>? resolve = null)
    {
        string? Kind(XElement e) => resolve?.Invoke(ConfigDocument.Attribute(e, "Type")) ?? Role(ConfigDocument.Attribute(e, "Type"));
        var nodes = module.Descendants("Setting").ToArray();
        var signals = nodes.Where(e => Kind(e) == "Sensor").ToDictionary(ConfigDocument.PathOf,
            e => new DiSensorDto { Name = ConfigDocument.Attribute(e, "Name"), IsActive = null });
        var tanks = nodes.Where(e => Kind(e) == "Tank").Select(e =>
        {
            string path = ConfigDocument.PathOf(e);
            DiSensorDto? Signal(string name) => signals.GetValueOrDefault(path + "." + name);
            var routes = e.Descendants("Setting").Where(g => Kind(g) == "ValveGroup").Select(g => new CdsRouteDefinition(
                ConfigDocument.Attribute(g, "Name").ToLowerInvariant(), g.Descendants("Setting").Where(v => Kind(v) == "Valve").Select(ConfigDocument.PathOf).ToArray())).ToArray();
            return new CdsTankSnapshot(new TankDto { Path = path, LL = Signal("LL"), L = Signal("L"), H = Signal("H"), HH = Signal("HH"), Overfill = Signal("Overfill") }, routes);
        }).ToArray();
        var devices = new List<CdsDeviceSnapshot>();
        foreach (var e in nodes)
        {
            string? kind = Kind(e); string path = ConfigDocument.PathOf(e), name = ConfigDocument.Attribute(e, "Name");
            if (kind is "Sensor" or "Valve" or "Analog") devices.Add(new(kind, path, name, signals.GetValueOrDefault(path)));
            else if (kind?.StartsWith("Hardware:", StringComparison.Ordinal) == true && Enum.TryParse<FluidHardwareKind>(kind[9..], out var hardware))
                devices.Add(new("Hardware", path, name, VisualKind: hardware));
        }
        return new CdsSnapshot(ConfigDocument.PathOf(module), tanks, devices);
    }
}
