using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Xml;
using System.Xml.Linq;
using xyz.Client.Presentation.Controls.ThreeD;

namespace CdsLayoutPreview;

public sealed class LayoutNode
{
    public string Kind { get; init; } = "";
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    public string Component { get; init; } = "";
    public string Direction { get; init; } = "Right";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; init; }
    public double Height { get; init; }
    public override string ToString() => Text + " / " + Id;

    public Transform3DGroup TankTransform()
    {
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 12)));
        transform.Children.Add(new ScaleTransform3D(Width / (TankVisual3D.RimRadius * 2), Height / TankVisual3D.TankHeight, Width / 2));
        transform.Children.Add(new TranslateTransform3D(X + Width / 2, -Y - Height, 0));
        return transform;
    }
}

public sealed class LayoutPipe
{
    public string Id { get; init; } = "";
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Medium { get; set; } = "IPA";
    public string Text { get; set; } = "管路";
    public string Mode { get; set; } = "supply";
    public PipeAppearance Appearance { get; set; } = new();
    public List<Point> Bends { get; } = [];
    public override string ToString() => "管路 / " + Id;
}

/// <summary>编辑数据与 WPF 渲染分离；XML 只保存布局，不包含实际控制命令。</summary>
public sealed class LayoutDocument
{
    public string Id { get; init; } = "IPA";
    public string Title { get; init; } = "IPA 供液";
    public string ComponentRoot { get; init; } = "CDS.IPA";
    public double Width { get; init; } = 1000;
    public double Height { get; init; } = 650;
    public List<LayoutNode> Nodes { get; } = [];
    public List<LayoutPipe> Pipes { get; } = [];

    public IEnumerable<string> PortNames => Nodes.SelectMany(node => node.Kind == "Tank"
        ? new[] { node.Id + ".Inlet", node.Id + ".Outlet", node.Id + ".Drain" }
        : new[] { node.Id + ".Port" });

    public Point3D Port3D(string name)
    {
        int separator = name.LastIndexOf('.');
        if (separator <= 0)
        {
            throw new InvalidDataException($"连接口格式错误：{name}");
        }

        var node = Nodes.SingleOrDefault(item => item.Id == name[..separator])
            ?? throw new InvalidDataException($"找不到连接口组件：{name}");
        string port = name[(separator + 1)..];
        if (node.Kind == "Terminal" && port == "Port")
        {
            return new Point3D(node.X, -node.Y, 0);
        }

        if (node.Kind != "Tank")
        {
            throw new InvalidDataException($"连接口不存在：{name}");
        }

        Point3D local;
        switch (port)
        {
            case "Inlet":
                local = new Point3D(-1, 2.13, 0);
                break;
            case "Outlet":
                local = new Point3D(1, 1.065, 0);
                break;
            case "Drain":
                local = new Point3D(0, 0, 0);
                break;
            default:
                throw new InvalidDataException($"连接口不存在：{name}");
        }

        return node.TankTransform().Transform(local);
    }

    public Point Port(string name)
    {
        var point = Port3D(name);
        return new Point(point.X, -point.Y);
    }

    public List<Point> Route(LayoutPipe pipe)
    {
        return [Port(pipe.From), .. pipe.Bends, Port(pipe.To)];
    }

    // 文字锚定在路径长度的一半处，路径变化后重新计算，不保存绝对坐标。
    public Point PipeTextPosition(LayoutPipe pipe)
    {
        var route = Route(pipe);
        double remaining = route.Zip(route.Skip(1), (start, end) => (end - start).Length).Sum() / 2;
        for (int i = 0; i < route.Count - 1; i++)
        {
            Vector delta = route[i + 1] - route[i];
            double length = delta.Length;
            if (length < 0.001)
            {
                continue;
            }
            if (remaining <= length)
            {
                Point anchor = route[i] + delta * (remaining / length);
                return anchor + (Math.Abs(delta.X) >= Math.Abs(delta.Y)
                    ? new Vector(8, -pipe.Appearance.Diameter / 2 - 24)
                    : new Vector(pipe.Appearance.Diameter / 2 + 8, -10));
            }
            remaining -= length;
        }
        return route[0] + new Vector(12, -24);
    }

    public void MoveNode(LayoutNode node, Point position)
    {
        var before = PortNames.ToDictionary(name => name, Port);
        double maxX = Math.Max(0, Width - Math.Max(30, node.Width));
        double maxY = Math.Max(0, Height - Math.Max(45, node.Height + 30));
        node.X = Math.Clamp(position.X, Math.Min(30, maxX), maxX);
        node.Y = Math.Clamp(position.Y, Math.Min(node.Kind == "Tank" ? 80 : 30, maxY), maxY);
        foreach (var pipe in Pipes)
        {
            Point oldStart = before[pipe.From];
            Point oldEnd = before[pipe.To];
            Point start = Port(pipe.From);
            Point end = Port(pipe.To);
            if (start == oldStart && end == oldEnd)
            {
                continue;
            }
            if (pipe.Bends.Count == 0)
            {
                if (Math.Abs(start.X - end.X) > 1 && Math.Abs(start.Y - end.Y) > 1)
                {
                    pipe.Bends.Add(Math.Abs(oldStart.X - oldEnd.X) < 1
                        ? new Point(start.X, end.Y) : new Point(end.X, start.Y));
                }
                continue;
            }

            // 保持原有首尾水平/垂直段跟随连接口；中间拐点仍由用户决定。
            if (start != oldStart)
            {
                pipe.Bends[0] = Follow(pipe.Bends[0], oldStart, start);
            }
            if (end != oldEnd)
            {
                int last = pipe.Bends.Count - 1;
                pipe.Bends[last] = Follow(pipe.Bends[last], oldEnd, end);
            }
        }
    }

    private static Point Follow(Point bend, Point before, Point after)
    {
        if (Math.Abs(bend.X - before.X) < 1)
        {
            bend.X = after.X;
        }
        if (Math.Abs(bend.Y - before.Y) < 1)
        {
            bend.Y = after.Y;
        }
        return bend;
    }

    public static LayoutDocument Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    public static LayoutDocument Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000
        });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != "CdsLayout" || Required(root, "Version") != "1" || root.Elements("Page").Count() != 1)
        {
            throw new InvalidDataException("需要 Version=1 且包含一个 Page 的 CdsLayout 文件。");
        }

        var page = root.Element("Page")!;
        var result = new LayoutDocument
        {
            Id = Required(page, "Id"), Title = Required(page, "Title"), ComponentRoot = Required(page, "ComponentRoot"),
            Width = Number(page, "Width"), Height = Number(page, "Height")
        };
        foreach (var item in (page.Element("Elements") ?? throw new InvalidDataException("缺少 Elements")).Elements())
        {
            bool tank = item.Name == "Tank";
            if (!tank && item.Name != "Terminal")
            {
                throw new InvalidDataException($"当前编辑器不支持图元：{item.Name}");
            }
            if (tank && Required(item, "Template") != "StandardTank")
            {
                throw new InvalidDataException("当前仅支持 StandardTank 模板。");
            }
            result.Nodes.Add(new LayoutNode
            {
                Kind = item.Name.LocalName, Id = Required(item, "Id"), Text = Required(item, "Text"),
                X = Number(item, "X"), Y = Number(item, "Y"),
                Width = tank ? Number(item, "Width") : 0, Height = tank ? Number(item, "Height") : 0,
                Component = tank ? Required(item, "Component") : "", Direction = (string?)item.Attribute("Direction") ?? "Right"
            });
        }

        foreach (var item in (page.Element("Pipes") ?? throw new InvalidDataException("缺少 Pipes")).Elements("Pipe"))
        {
            var pipe = new LayoutPipe
            {
                Id = Required(item, "Id"), From = Required(item, "From"), To = Required(item, "To"),
                Medium = Required(item, "Medium"), Text = (string?)item.Attribute("Text") ?? Required(item, "Id"),
                Mode = Required(item, "PreviewMode"), Appearance = PipeAppearance.Read(item)
            };
            foreach (var bend in item.Element("Bends")?.Elements("Point") ?? [])
            {
                pipe.Bends.Add(new Point(Number(bend, "X"), Number(bend, "Y")));
            }
            result.Pipes.Add(pipe);
        }
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width < 300 || Height < 300 || Width > 4000 || Height > 4000)
        {
            throw new InvalidDataException("画布尺寸应在 300～4000 之间。");
        }
        if (!Nodes.Any(node => node.Kind == "Tank") || Nodes.Count > 100 || Pipes.Count > 200)
        {
            throw new InvalidDataException("布局至少需要一个槽，最多 100 个图元和 200 条管路。");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || node.Id.Contains('.') || !ids.Add(node.Id))
            {
                throw new InvalidDataException($"图元 ID 为空、包含句点或重复：{node.Id}");
            }
            CheckPoint(new Point(node.X, node.Y));
            if (node.Kind == "Tank" && (!double.IsFinite(node.Width) || !double.IsFinite(node.Height)
                || node.Width < 40 || node.Height < 40 || node.X + node.Width > Width || node.Y + node.Height > Height))
            {
                throw new InvalidDataException($"槽的尺寸或位置不合法：{node.Id}");
            }
        }
        foreach (var pipe in Pipes)
        {
            pipe.Appearance.Validate();
            if (pipe.Text.Length > 120 || pipe.Text.Any(char.IsControl))
            {
                throw new InvalidDataException("管路文字最多 120 个字符，不能包含控制字符。");
            }
            if (string.IsNullOrWhiteSpace(pipe.Id) || !ids.Add(pipe.Id) || pipe.From == pipe.To
                || pipe.Mode is not ("fill" or "supply" or "drain") || pipe.Bends.Count > 100)
            {
                throw new InvalidDataException($"管路 ID、连接口、模拟状态或拐点数量不合法：{pipe.Id}");
            }
            foreach (var point in Route(pipe))
            {
                CheckPoint(point);
            }
        }
    }

    private void CheckPoint(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X < 0 || point.Y < 0 || point.X > Width || point.Y > Height)
        {
            throw new InvalidDataException("图元或拐点坐标无效，或超出画布。");
        }
    }

    public string ToXml()
    {
        var elements = new XElement("Elements");
        foreach (var node in Nodes)
        {
            var item = new XElement(node.Kind, new XAttribute("Id", node.Id), new XAttribute("Text", node.Text),
                new XAttribute("X", node.X), new XAttribute("Y", node.Y));
            if (node.Kind == "Tank")
            {
                item.Add(new XAttribute("Component", node.Component), new XAttribute("Width", node.Width),
                    new XAttribute("Height", node.Height), new XAttribute("Template", "StandardTank"));
            }
            else
            {
                item.Add(new XAttribute("Direction", node.Direction));
            }
            elements.Add(item);
        }
        var pipes = new XElement("Pipes");
        foreach (var pipe in Pipes)
        {
            var item = new XElement("Pipe", new XAttribute("Id", pipe.Id), new XAttribute("From", pipe.From),
                new XAttribute("To", pipe.To), new XAttribute("Medium", pipe.Medium), new XAttribute("Text", pipe.Text), new XAttribute("PreviewMode", pipe.Mode));
            pipe.Appearance.Write(item);
            if (pipe.Bends.Count > 0)
            {
                item.Add(new XElement("Bends", pipe.Bends.Select(point => new XElement("Point",
                    new XAttribute("X", point.X), new XAttribute("Y", point.Y)))));
            }
            pipes.Add(item);
        }
        return new XDocument(new XElement("CdsLayout", new XAttribute("Version", "1"),
            new XElement("Page", new XAttribute("Id", Id), new XAttribute("Title", Title),
                new XAttribute("ComponentRoot", ComponentRoot), new XAttribute("Width", Width), new XAttribute("Height", Height),
                elements, pipes))).ToString();
    }

    public void Save(string path)
    {
        Validate();
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, ToXml());
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string Required(XElement element, string name)
    {
        return (string?)element.Attribute(name) ?? throw new InvalidDataException($"{element.Name} 缺少 {name}");
    }

    private static double Number(XElement element, string name)
    {
        if (!double.TryParse(Required(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            throw new InvalidDataException($"{element.Name} 的 {name} 不是有效数值。");
        }
        return value;
    }
}

public sealed class LayoutHistory(LayoutDocument document)
{
    public event Action? Changed;
    private readonly List<string> _undo = [];
    private readonly Stack<string> _redo = [];
    private string _saved = document.ToXml();
    public LayoutDocument Document { get; private set; } = document;
    public bool IsDirty => Document.ToXml() != _saved;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Commit(string before)
    {
        if (before == Document.ToXml())
        {
            return;
        }
        _undo.Add(before);
        if (_undo.Count > 60)
        {
            _undo.RemoveAt(0);
        }
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }
        _redo.Push(Document.ToXml());
        Document = LayoutDocument.Parse(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }
        _undo.Add(Document.ToXml());
        Document = LayoutDocument.Parse(_redo.Pop());
        Changed?.Invoke();
    }

    public void Restore(string snapshot)
    {
        Document = LayoutDocument.Parse(snapshot);
        Changed?.Invoke();
    }

    public void Load(LayoutDocument value)
    {
        Document = value;
        _undo.Clear();
        _redo.Clear();
        MarkSaved();
    }

    public void MarkSaved()
    {
        _saved = Document.ToXml();
        Changed?.Invoke();
    }
}
