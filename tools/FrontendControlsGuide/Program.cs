using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Xml.Linq;
using xyz.Client.Presentation.Controls;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace FrontendControlsGuide;

internal static partial class Program
{
    private static string _root = "", _output = "";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Dictionary<string, SourceInfo> Sources = [];
    private static readonly List<string> Errors = [];

    [STAThread]
    private static int Main(string[] args)
    {
        _root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? @"D:\Code");
        _output = Path.Combine(_root, "docs", "frontend-controls"); Directory.CreateDirectory(_output);
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            var app = new Application(); LoadResources(app);
            ReadSources(Path.Combine(_root, "xyz.Core", "Client", "Common", "xyz.Client.Presentation", "Controls"));
            ReadSources(Path.Combine(_root, "xyz.35021", "Client", "Presentation", "Controls"));
            var types = new[] { typeof(SignalLamp).Assembly, typeof(xyz._35021.Client.Presentation.Controls.ChamberView).Assembly }
                .SelectMany(a => a.GetExportedTypes()).Where(t => !t.IsAbstract && (typeof(FrameworkElement).IsAssignableFrom(t) || typeof(Visual3D).IsAssignableFrom(t))
                    && Sources.ContainsKey(t.FullName!)).OrderBy(t => Order(t.Name)).ThenBy(t => t.Name).ToArray();
            var controls = new List<ControlInfo>();
            foreach (var type in types)
            {
                var source = Sources[type.FullName!];
                var entry = new ControlInfo { Name = type.Name, Namespace = type.Namespace!, Source = source.File,
                    Summary = source.Summary, Is3D = typeof(Visual3D).IsAssignableFrom(type) || type.Name is "Robot3D" or "BufferView" or "ChamberView" || type.Name.EndsWith("Scene"),
                    Project = type.Namespace!.StartsWith("xyz._35021", StringComparison.Ordinal), Properties = Properties(type), Members = source.Members };
                controls.Add(entry);
                try { ExportControl(type, entry); }
                catch (Exception ex) { entry.Error = ex.GetBaseException().Message; Errors.Add(type.Name + ": " + ex); }
                Console.WriteLine(type.Name + " " + entry.Properties.Count + " DP · " + entry.Variants.Count + " examples" + (entry.Error is null ? "" : " ERROR " + entry.Error));
            }
            var catalog = new { controls, styles = StyleDependencies(), schemas = Schemas(), resourceFiles = ResourceFiles };
            File.WriteAllText(Path.Combine(_output, "catalog.json"), JsonSerializer.Serialize(catalog, new JsonSerializerOptions(Json) { WriteIndented = true }));
            File.WriteAllText(Path.Combine(_output, "catalog.js"), "window.FRONTEND_CONTROLS=" + JsonSerializer.Serialize(catalog, Json) + ";");
            File.WriteAllLines(Path.Combine(_output, "export-errors.txt"), Errors);
            app.Shutdown(); return Errors.Count == 0 ? 0 : 2;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(_output, "export-error.log"), ex.ToString()); return 1; }
    }

    private static readonly string[] ResourceFiles = ["Color", "DarkColors", "FontSize", "TextBlockStyles", "BorderStyles", "TextBoxStyles", "ComboBoxStyles", "DatePickerStyles", "ButtonStyles", "CardStyles", "DataGridStyles", "ListBoxStyles", "TreeViewStyles"];
    private static void LoadResources(Application app)
    {
        app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme { BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark, PrimaryColor = MaterialDesignColors.PrimaryColor.Blue, SecondaryColor = MaterialDesignColors.SecondaryColor.Lime });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml") });
        foreach (string file in ResourceFiles) app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/xyz.Client.Presentation;component/Styles/{file}.xaml") });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/xyz.Client.Presentation;component/Localization/Strings.zh-CN.xaml") });
    }
    private static void ReadSources(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            var unit = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            string ns = unit.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";
            foreach (var declaration in unit.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => c.Parent is not ClassDeclarationSyntax))
            {
                string key = ns + "." + declaration.Identifier.Text;
                if (!Sources.TryGetValue(key, out var source)) Sources[key] = source = new SourceInfo { File = Path.GetRelativePath(_root, file).Replace('\\', '/'), Summary = Doc(declaration) };
                foreach (var p in declaration.Members.OfType<PropertyDeclarationSyntax>().Where(p => p.Modifiers.Any(SyntaxKind.PublicKeyword)))
                { var doc = Doc(p); if (doc.Length > 0) source.Docs[p.Identifier.Text] = doc;
                    if (!p.ToString().Contains("GetValue(")) source.Members.Add(new MemberInfoDoc(p.Identifier.Text, p.Type.ToString(), doc)); }
                foreach (var f in declaration.Members.OfType<FieldDeclarationSyntax>()) foreach (var v in f.Declaration.Variables)
                    if (Doc(f).Length > 0 && !source.Docs.ContainsKey(v.Identifier.Text.Replace("Property", ""))) source.Docs[v.Identifier.Text.Replace("Property", "")] = Doc(f);
            }
        }
    }
    private static string Doc(SyntaxNode node)
    {
        string text = string.Join("\n", node.GetLeadingTrivia().ToFullString().Split('\n').Select(s => s.Trim()).Where(s => s.StartsWith("///")).Select(s => s[3..]));
        if (text.Length == 0) return "";
        try { var root = XElement.Parse("<doc>" + text + "</doc>"); foreach (var see in root.Descendants("see").ToArray()) see.ReplaceWith((string?)see.Attribute("cref") ?? "");
            return Regex.Replace(root.Element("summary")?.Value ?? "", @"\s+", " ").Trim(); }
        catch { return Regex.Replace(text, "<[^>]+>", "").Trim(); }
    }
    private static List<PropertyInfoDoc> Properties(Type type)
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        var result = new List<PropertyInfoDoc>(); var seen = new HashSet<string>();
        for (var current = type; current is not null && Sources.ContainsKey(current.FullName!); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(f => f.FieldType == typeof(DependencyProperty)))
            {
                if (field.GetValue(null) is not DependencyProperty dp || !seen.Add(dp.Name)) continue;
                var metadata = dp.GetMetadata(type); string description = Sources.GetValueOrDefault(current.FullName!)?.Docs.GetValueOrDefault(dp.Name) ?? "";
                if (description.Length == 0) description = FallbackProperty(dp.Name);
                result.Add(new PropertyInfoDoc(dp.Name, FriendlyType(dp.PropertyType), Value(metadata.DefaultValue), dp.ReadOnly,
                    metadata is FrameworkPropertyMetadata { BindsTwoWayByDefault: true }, current.Name, description,
                    dp.PropertyType.IsEnum ? Enum.GetNames(dp.PropertyType) : Nullable.GetUnderlyingType(dp.PropertyType) is { IsEnum: true } e ? Enum.GetNames(e) : []));
            }
        }
        return result;
    }
    private static string FriendlyType(Type type) => Nullable.GetUnderlyingType(type) is { } underlying ? FriendlyType(underlying) + "?" : type.IsGenericType
        ? type.Name.Split('`')[0] + "<" + string.Join(", ", type.GetGenericArguments().Select(FriendlyType)) + ">" : type.Name;
    private static string Value(object? value) => value switch
    { null => "null", string s => s.Length == 0 ? "空字符串" : s, bool b => b ? "true" : "false", Color c => c.ToString(), Brush b => b.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() ?? "null" };
    private static int Order(string name) => name switch { "InputTextBox" => 0, "DateTimePicker" => 1, "PickerBox" => 2, "HoldButton" => 3,
        "SignalLamp" => 4, "SignalTower" => 5, "TrendChart" => 6, "Wafer" => 7, "ArmVisual3D" => 30, "BowlVisual3D" => 31,
        "LiftVisual3D" => 32, "TankVisual3D" => 33, "FluidRouteVisual3D" => 34, _ => name.Contains("Visual3D") ? 40 : name.EndsWith("Scene") ? 50 : 15 };
    private static object StyleDependencies() => ResourceFiles;
    private static object Schemas() => new[] { "TankModel", "DiSensorModel", "WaferModel", "RobotArmModel", "TrendSeries", "ProgressState" };

    private sealed class SourceInfo { public string File = "", Summary = ""; public Dictionary<string, string> Docs = []; public List<MemberInfoDoc> Members = []; }
    private sealed class ControlInfo
    {
        public string Name { get; set; } = ""; public string Namespace { get; set; } = ""; public string Source { get; set; } = ""; public string Summary { get; set; } = "";
        public bool Is3D { get; set; } public bool Project { get; set; } public List<PropertyInfoDoc> Properties { get; set; } = [];
        public List<MemberInfoDoc> Members { get; set; } = []; public List<VariantInfo> Variants { get; set; } = []; public string? Error { get; set; }
    }
    private sealed record PropertyInfoDoc(string Name, string Type, string Default, bool ReadOnly, bool TwoWay, string Owner, string Description, string[] EnumValues);
    private sealed record MemberInfoDoc(string Name, string Type, string Description);
    private sealed record VariantInfo(string Label, Dictionary<string, string> Values, string Image, string? Mesh);
    private sealed record MeshInfo(float[] Positions, int[] Indices, float[] Color);
}
