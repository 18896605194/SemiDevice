using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using CdsLayoutPreview;
using ToolBuilder.Core;

namespace ToolBuilder;

public sealed class CdsLayoutViewModel : Observable
{
    private readonly NodeModel _module;
    private string _path;
    private byte[]? _fingerprint;
    private bool _isNew;
    public PreviewPage Editor { get; }
    public string Description => $"{_module.Path} · {Editor.Document.Nodes.Count(node => node.Kind == "Tank")} 个槽 · 模拟预览 · 顶部保存同步保存 machine.xml 和布局";
    public string FilePath => _path;
    public bool IsDirty => _isNew || Editor.HasUnsavedChanges;
    public event Action? Changed;

    public static bool Supports(NodeModel? node)
    {
        return node is not null && node.Type.Length > 0 && !IsTank(node)
            && node.Walk().Any(IsTank);
    }

    private static bool IsTank(NodeModel node)
    {
        return node.Type.Split(',')[0].Trim().EndsWith(".TankElement", StringComparison.Ordinal);
    }

    public CdsLayoutViewModel(NodeModel module, string machinePath)
    {
        _module = module;
        _path = LayoutPath(machinePath, module.Path);
        _isNew = !File.Exists(_path);
        var document = _isNew ? CreateLayout(module) : LayoutDocument.Load(_path);
        _fingerprint = _isNew ? null : SHA256.HashData(File.ReadAllBytes(_path));
        Editor = new PreviewPage(document, CreateTheme());
        Editor.ConfigureEmbedded();
        Editor.DocumentChanged += () => Changed?.Invoke();
    }

    public static string LayoutPath(string machinePath, string modulePath)
    {
        // 转义路径片段，避免 machine.xml 中的节点名称变成目录穿越或非法文件名。
        return Path.Combine(Path.GetDirectoryName(machinePath)!, "CdsLayouts", Path.GetFileNameWithoutExtension(machinePath),
            Uri.EscapeDataString(modulePath) + ".xml");
    }

    public FileWrite PrepareWrite(string machinePath)
    {
        string xml = Editor.PrepareSave();
        ValidateBindings();
        string path = LayoutPath(machinePath, _module.Path);
        var parsed = XDocument.Parse(xml);
        parsed.Root!.Element("Page")!.SetAttributeValue("ComponentRoot", _module.Path);
        byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(parsed.ToString());
        byte[]? expected = string.Equals(path, _path, StringComparison.OrdinalIgnoreCase) ? _fingerprint : null;
        return new FileWrite(path, bytes, expected);
    }

    public void Validate()
    {
        if (Editor.HasDraft)
        {
            throw new InvalidDataException("还有未完成的管路，请连接终点或按 Esc 取消。");
        }
        if (Editor.ViewModel.PipeProperties.HasPendingChanges && !Editor.ViewModel.PipeProperties.TryRead(out _, out _))
        {
            throw new InvalidDataException(Editor.ViewModel.PipeProperties.Error);
        }
        Editor.Document.Validate();
        ValidateBindings();
    }

    private void ValidateBindings()
    {
        var configured = _module.Walk().Where(IsTank).Select(node => RelativePath(node)).ToHashSet(StringComparer.Ordinal);
        if (!Editor.Document.Nodes.Where(node => node.Kind == "Tank").Select(node => node.Component).ToHashSet(StringComparer.Ordinal).SetEquals(configured))
        {
            throw new InvalidDataException($"{_module.Path} 的槽元件与布局不一致。请恢复 machine.xml 中的槽名称/数量，或重新打开并重新配置布局。");
        }
    }

    public void AcceptSave(FileWrite write)
    {
        _path = write.Path;
        _fingerprint = SHA256.HashData(write.Content);
        _isNew = false;
        Editor.AcceptSave(_path);
        Raise(nameof(Description));
        Raise(nameof(FilePath));
        Changed?.Invoke();
    }

    private string RelativePath(NodeModel node) => node.Path[(_module.Path.Length + 1)..];

    private static LayoutDocument CreateLayout(NodeModel module)
    {
        var tanks = module.Walk().Where(IsTank).ToList();
        if (tanks.Count is < 1 or > 24)
        {
            throw new InvalidDataException("管路布局支持 1～24 个槽。");
        }
        int columns = Math.Min(3, tanks.Count);
        int rows = (tanks.Count + columns - 1) / columns;
        var document = new LayoutDocument
        {
            Id = module.Name, Title = module.Name + " 管路布局", ComponentRoot = module.Path,
            Width = columns * 650, Height = rows * 490
        };
        for (int i = 0; i < tanks.Count; i++)
        {
            string id = "Tank" + (i + 1);
            double x = i % columns * 650;
            double y = i / columns * 490;
            document.Nodes.Add(new LayoutNode
            {
                Kind = "Tank", Id = id, Text = tanks[i].Name, Component = tanks[i].Path[(module.Path.Length + 1)..],
                X = x + 220, Y = y + 100, Width = 160, Height = 230
            });
            document.Nodes.Add(new LayoutNode { Kind = "Terminal", Id = id + "Fill", Text = "补液接口", X = x + 55, Y = y + 153 });
            document.Nodes.Add(new LayoutNode { Kind = "Terminal", Id = id + "Supply", Text = "供液接口", X = x + 580, Y = y + 242 });
            document.Nodes.Add(new LayoutNode { Kind = "Terminal", Id = id + "Drain", Text = "排液接口", X = x + 300, Y = y + 435 });
            document.Pipes.Add(new LayoutPipe { Id = id + "FillPipe", Text = "补液", From = id + "Fill.Port", To = id + ".Inlet", Mode = "fill" });
            document.Pipes.Add(new LayoutPipe { Id = id + "SupplyPipe", Text = "供液", From = id + ".Outlet", To = id + "Supply.Port", Mode = "supply" });
            document.Pipes.Add(new LayoutPipe { Id = id + "DrainPipe", Text = "排液", From = id + ".Drain", To = id + "Drain.Port", Mode = "drain" });
            // 初始端点与原生槽的实际投影坐标对齐。
            document.Nodes[^3].Y = document.Port(id + ".Inlet").Y;
            document.Nodes[^2].Y = document.Port(id + ".Outlet").Y;
        }
        foreach (var pipe in document.Pipes)
        {
            pipe.Appearance = pipe.Appearance with { WallOpacity = 0.65, WallColor = (Color)ColorConverter.ConvertFromString("#66768B") };
        }
        document.Validate();
        return document;
    }

    private static ResourceDictionary CreateTheme()
    {
        var theme = new ResourceDictionary();
        foreach (string name in new[] { "DarkColors" })
        {
            theme.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/xyz.Client.Presentation;component/Styles/{name}.xaml")
            });
        }
        foreach (var (key, color) in new Dictionary<string, string>
        {
            ["DarkWindowBackground"] = "#FFFFFF", ["DarkSurfaceBackground"] = "#F8FAFD",
            ["DarkControlBackground"] = "#FFFFFF", ["DarkBorderBrush"] = "#D5DDE9",
            ["DarkPrimaryText"] = "#23324A", ["DarkSecondaryText"] = "#23324A",
            ["DarkDisabledText"] = "#63738C", ["DarkAccent"] = "#2463EB", ["DarkAccentPressed"] = "#DFEBFF"
        })
        {
            theme[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
        // 只给原生信号灯提供局部样式，不导入依赖全局暗色资源的整套框架样式。
        var textStyle = new Style(typeof(System.Windows.Controls.TextBlock));
        textStyle.Setters.Add(new Setter(System.Windows.Controls.TextBlock.ForegroundProperty, theme["DarkPrimaryText"]));
        textStyle.Setters.Add(new Setter(System.Windows.Controls.TextBlock.FontSizeProperty, 12d));
        textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0)));
        theme["SignalLampTextStyle"] = textStyle;
        var lampStyle = new Style(typeof(System.Windows.Controls.Border));
        lampStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 14d));
        lampStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 14d));
        lampStyle.Setters.Add(new Setter(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(7)));
        lampStyle.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, Brushes.LightGray));
        foreach (var (tag, brush) in new[] { ("On", Brushes.SeaGreen), ("Alarm", Brushes.IndianRed), ("Unknown", Brushes.Goldenrod) })
        {
            var trigger = new Trigger { Property = FrameworkElement.TagProperty, Value = tag };
            trigger.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, brush));
            lampStyle.Triggers.Add(trigger);
        }
        theme["SignalLampBorderStyle"] = lampStyle;
        return theme;
    }
}
