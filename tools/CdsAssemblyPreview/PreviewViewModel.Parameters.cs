using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace CdsAssemblyPreview;

public sealed partial class PreviewViewModel
{
    #region Column
    public ObservableCollection<CdsEditorParameter> Parameters { get; } = [];
    public ObservableCollection<CdsNumberParameter> BendParameters { get; } = [];
    private int _selectedInspectorTab;
    public int SelectedInspectorTab
    {
        get => _selectedInspectorTab;
        set
        {
            if (!SetProperty(ref _selectedInspectorTab, value)) return;
            OnPropertyChanged(nameof(IsSimulationMode));
            if (IsSimulationMode) { ConnectMode = false; _startPort = null; ClearAlignmentGuide(); }
            SceneChanged?.Invoke();
        }
    }
    public bool IsSimulationMode => SelectedInspectorTab == 2;
    public bool IsSensorSelected => Selected?.Kind == "Sensor";
    public bool IsHardwareSelected => Selected?.Kind is "Hardware" or "Analog";
    public HardwareDemo? SelectedHardware => HardwareStates.GetValueOrDefault(SelectionPath);
    public bool IsHeaterSelected => Selected?.VisualKind == FluidHardwareKind.InlineHeater;
    public bool HasMotion => IsValveSelected || IsTankSelected || IsSensorSelected || IsHardwareSelected;
    public string SensorStateText => L10n.Get("cdsp.sensor." + (SelectedSensor?.IsActive switch { true => "on", false => "off", _ => "unknown" }));
    #endregion

    private DiSensorDto? SelectedSensor => Selected is null ? null : FindSensor(Selected.Component);
    public DiSensorDto? FindSensor(string component)
    {
        string full = Snapshot.ComponentRoot + "." + CdsLayoutComposer.Relative(Snapshot.ComponentRoot, component);
        return Snapshot.Sensors.FirstOrDefault(s => s.Path == full).Signal;
    }
    private void DoSetSensor(bool? value)
    {
        if (SelectedSensor is not { } signal) return;
        signal.IsActive = value; OnPropertyChanged(nameof(SensorStateText)); SceneChanged?.Invoke();
    }
    private void BuildParameters()
    {
        Parameters.Clear(); BendParameters.Clear();
        if (CurrentNode is { } node)
        {
            string id = node.Id;
            void Number(string key, double value, double min, double max, Func<CdsNode, double, CdsNode> change, string unit = "cdsp.units.canvas")
                => Parameters.Add(new CdsNumberParameter(key, value, min, max, v =>
                { var current = Layout.Nodes.Single(n => n.Id == id); ReplaceNode(change(current, v)); }, unit));
            Number("Width", node.Width, node.Kind == "Tank" ? 40 : 8, Layout.Width,
                (n, v) => n with { Width = Math.Min(v, n.Kind == "Terminal" ? Layout.Width : Layout.Width - n.X) });
            Number("Height", node.Height, node.Kind == "Tank" ? 40 : 8, Layout.Height,
                (n, v) => n with { Height = Math.Min(v, n.Kind == "Terminal" ? Layout.Height : Layout.Height - n.Y) });
            Number("Angle", node.Angle, -360, 360, (n, v) => n with { Angle = v }, "cdsp.units.angle");
            if (node.Kind == "Tank") Number("WallTransparency", node.WallTransparency, 0, 1, (n, v) => n with { WallTransparency = v }, "cdsp.units.ratio");
            if (node.Kind == "Sensor") Parameters.Add(new CdsBooleanParameter("SensorAlarm", node.SensorAlarm,
                v => ReplaceNode(Layout.Nodes.Single(n => n.Id == id) with { SensorAlarm = v })));
            Parameters.Add(new CdsTextParameter("Caption", node.Text, text =>
            {
                if (text.Length > 120 || text.Any(char.IsControl)) return L10n.Get("cdsp.caption.invalid");
                ReplaceNode(Layout.Nodes.Single(n => n.Id == id) with { Text = text }); return null;
            }));
        }
        if (SelectedPipe is { } pipe)
        {
            string id = pipe.Id;
            void Number(string key, double value, double min, double max, Func<CdsPipe, double, CdsPipe> change, string unit = "cdsp.units.canvas")
                => Parameters.Add(new CdsNumberParameter(key, value, min, max, v =>
                { var current = Layout.Pipes.Single(p => p.Id == id); ReplacePipe(change(current, v)); SyncEditors(); }, unit));
            Number("Diameter", pipe.Diameter, 2, 80, (p, v) => p with { Diameter = v, WallThickness = Math.Min(p.WallThickness, v / 2 - 0.1) });
            Number("WallThickness", pipe.WallThickness, 0.1, pipe.Diameter / 2 - 0.1, (p, v) => p with { WallThickness = Math.Min(v, p.Diameter / 2 - 0.1) });
            Number("BendRadius", pipe.BendRadius, 0, 300, (p, v) => p with { BendRadius = v });
            Number("WallOpacity", pipe.WallOpacity, 0, 1, (p, v) => p with { WallOpacity = v }, "cdsp.units.ratio");
            Number("FluidCoreRatio", pipe.FluidCoreRatio, 0.01, 1, (p, v) => p with { FluidCoreRatio = v }, "cdsp.units.ratio");
            Number("FlowSpeed", pipe.FlowSpeed, 0, 500, (p, v) => p with { FlowSpeed = v }, "cdsp.units.speed");
            void Flag(string key, bool value, Func<CdsPipe, bool, CdsPipe> change) => Parameters.Add(new CdsBooleanParameter(key, value,
                v => ReplacePipe(change(Layout.Pipes.Single(p => p.Id == id), v))));
            Flag("ReverseFlow", pipe.ReverseFlow, (p, v) => p with { ReverseFlow = v });
            Flag("IsFilled", pipe.IsFilled, (p, v) => p with { IsFilled = v });
            Flag("IsAnimationEnabled", pipe.IsAnimationEnabled, (p, v) => p with { IsAnimationEnabled = v });
            void Color(string key, System.Windows.Media.Color value, Func<CdsPipe, System.Windows.Media.Color, CdsPipe> change)
                => Parameters.Add(new CdsTextParameter(key, value.ToString(), text =>
                {
                    try { ReplacePipe(change(Layout.Pipes.Single(p => p.Id == id), (System.Windows.Media.Color)ColorConverter.ConvertFromString(text.Trim()))); return null; }
                    catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { return L10n.Get("cdsp.color.invalid"); }
                }));
            Color("WallColor", pipe.WallColor, (p, v) => p with { WallColor = v }); Color("FlowColor", pipe.FlowColor, (p, v) => p with { FlowColor = v });
            SyncBendParameters();
        }
    }
    private void SyncBendParameters()
    {
        if (SelectedPipe is not { } pipe) return;
        if (BendParameters.Count != pipe.Bends.Count * 2)
        {
            BendParameters.Clear();
            for (int i = 0; i < pipe.Bends.Count; i++)
            {
                int index = i; string id = pipe.Id;
                BendParameters.Add(new CdsNumberParameter($"{i + 1}.X", pipe.Bends[i].X, 0, Layout.Width,
                    v => { var p = Layout.Pipes.Single(p => p.Id == id); MovePipeBend(id, index, new Point(v, p.Bends[index].Y)); }));
                BendParameters.Add(new CdsNumberParameter($"{i + 1}.Y", pipe.Bends[i].Y, 0, Layout.Height,
                    v => { var p = Layout.Pipes.Single(p => p.Id == id); MovePipeBend(id, index, new Point(p.Bends[index].X, v)); }));
            }
        }
        for (int i = 0; i < pipe.Bends.Count; i++) { BendParameters[i * 2].Sync(pipe.Bends[i].X); BendParameters[i * 2 + 1].Sync(pipe.Bends[i].Y); }
    }
    private void SyncNumericParameters()
    {
        foreach (var row in Parameters.OfType<CdsNumberParameter>())
        {
            if (CurrentNode is { } node)
            {
                if (row.Key == "Width") row.Maximum = node.Kind == "Terminal" ? Layout.Width : Layout.Width - node.X;
                if (row.Key == "Height") row.Maximum = node.Kind == "Terminal" ? Layout.Height : Layout.Height - node.Y;
                row.Sync(row.Key switch { "Width" => node.Width, "Height" => node.Height, "Angle" => node.Angle,
                    "WallTransparency" => node.WallTransparency, _ => row.Value });
            }
            if (SelectedPipe is { } pipe)
            {
                if (row.Key == "WallThickness") row.Maximum = pipe.Diameter / 2 - 0.1;
                row.Sync(row.Key switch { "Diameter" => pipe.Diameter, "WallThickness" => pipe.WallThickness, "BendRadius" => pipe.BendRadius,
                    "WallOpacity" => pipe.WallOpacity, "FluidCoreRatio" => pipe.FluidCoreRatio, "FlowSpeed" => pipe.FlowSpeed, _ => row.Value });
            }
        }
    }
    public Point PipeCenter(CdsPipe pipe)
    {
        if (pipe.Bends.Count > 0) return new Point((pipe.Bends.Min(p => p.X) + pipe.Bends.Max(p => p.X)) / 2, (pipe.Bends.Min(p => p.Y) + pipe.Bends.Max(p => p.Y)) / 2);
        var from = Layout.Port(pipe.From); var to = Layout.Port(pipe.To); return new Point((from.X + to.X) / 2, -(from.Y + to.Y) / 2);
    }
    public void MovePipe(string id, Point position)
    {
        var pipe = Layout.Pipes.Single(p => p.Id == id); var center = PipeCenter(pipe);
        var from = Layout.Port(pipe.From); var to = Layout.Port(pipe.To);
        var bends = pipe.Bends.Count > 0 ? pipe.Bends.ToArray() : new[]
        { new Point(from.X * 0.65 + to.X * 0.35, -(from.Y * 0.65 + to.Y * 0.35)), new Point(from.X * 0.35 + to.X * 0.65, -(from.Y * 0.35 + to.Y * 0.65)) };
        double dx = Math.Clamp(position.X - center.X, -bends.Min(p => p.X), Layout.Width - bends.Max(p => p.X));
        double dy = Math.Clamp(position.Y - center.Y, -bends.Min(p => p.Y), Layout.Height - bends.Max(p => p.Y));
        ReplacePipe(pipe with { Bends = bends.Select(p => new Point(p.X + dx, p.Y + dy)).ToArray() }); SyncEditors(); SyncBendParameters();
    }
    public void MovePipeBend(string id, int index, Point position)
    {
        var pipe = Layout.Pipes.Single(p => p.Id == id); var bends = pipe.Bends.ToArray();
        bends[index] = new Point(Math.Clamp(position.X, 0, Layout.Width), Math.Clamp(position.Y, 0, Layout.Height));
        ReplacePipe(pipe with { Bends = bends }); SyncEditors(); SyncBendParameters();
    }
}
