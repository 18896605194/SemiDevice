using xyz.Client.Presentation.Localization;

namespace CdsAssemblyPreview;

// 预览 VM 的显示数据示例；项目 VM 提供同名属性即可直接绑定，不需要布局运行时。
public sealed record CdsDiagramState(double FillFraction = 0, bool IsAlarm = false, bool? IsOpen = null, bool? IsActive = null,
    HardwareState State = HardwareState.Unknown, double? Reading = null, double? Target = null, double PowerFraction = 0, string Unit = "", string ReadingText = "");
public sealed record CdsDiagramPipeState(bool IsFlowing, bool IsFilled);

public sealed partial class PreviewViewModel
{
    #region Column
    private IReadOnlyDictionary<string, CdsDiagramState> _diagramStates = new Dictionary<string, CdsDiagramState>();
    public IReadOnlyDictionary<string, CdsDiagramState> DiagramStates { get => _diagramStates; private set => SetProperty(ref _diagramStates, value); }
    private IReadOnlyDictionary<string, CdsDiagramPipeState> _diagramPipes = new Dictionary<string, CdsDiagramPipeState>();
    public IReadOnlyDictionary<string, CdsDiagramPipeState> DiagramPipes { get => _diagramPipes; private set => SetProperty(ref _diagramPipes, value); }
    #endregion

    private void RefreshDiagramStates()
    {
        var states = new Dictionary<string, CdsDiagramState>(StringComparer.Ordinal);
        foreach (var node in Layout.Nodes.Where(n => n.Kind != "Terminal"))
        {
            string path = Snapshot.ComponentRoot + "." + node.Component;
            var state = new CdsDiagramState();
            if (node.Kind == "Tank")
            {
                int level = Math.Clamp(Snapshot.Tanks.FirstOrDefault(t => t.Data.Path == path)?.Data.Level ?? 0, 0, 6);
                double[] fractions = [0, .05, .205, .47, .72, .865, .998];
                state = state with { FillFraction = fractions[level], IsAlarm = level >= 5, ReadingText = L10n.Get("cdsp.level." + level) };
            }
            else if (node.Kind == "Valve") state = state with { IsOpen = ValveStates.GetValueOrDefault(path) };
            else if (node.Kind == "Sensor") state = state with { IsActive = FindSensor(node.Component)?.IsActive };
            else if (HardwareStates.GetValueOrDefault(path) is { } hardware)
                state = state with { State = hardware.State, Reading = hardware.Reading, Target = hardware.Target, Unit = hardware.Unit, PowerFraction = hardware.PowerFraction,
                    ReadingText = HardwareVisual.FormatReading(hardware.Reading, hardware.Unit, hardware.State) };
            states[node.Component] = state;
        }
        var pipes = new Dictionary<string, CdsDiagramPipeState>(StringComparer.Ordinal);
        foreach (var pipe in Layout.Pipes)
        {
            var valves = new[] { pipe.From, pipe.To }.Select(CdsLayoutComposer.Owner).Distinct().Select(id => Layout.Nodes.FirstOrDefault(n => n.Id == id)).Where(n => n?.Kind == "Valve").ToArray();
            bool flowing = valves.Length > 0 && valves.All(n => ValveStates.GetValueOrDefault(Snapshot.ComponentRoot + "." + n!.Component) == true);
            pipes[pipe.Id] = new(flowing, pipe.IsFilled && flowing);
        }
        DiagramStates = states; DiagramPipes = pipes;
    }
}
