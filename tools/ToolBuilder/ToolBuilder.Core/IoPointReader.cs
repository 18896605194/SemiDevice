using System.Globalization;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;

namespace ToolBuilder.Core;

public sealed record IoPointChoice(string Index, string Module, string Component, string Name, string Description);

public static class IoPointReader
{
    public static string? KindFor(string propertyName)
    {
        foreach (var kind in new[] { "Di", "Do", "Ai", "Ao" })
            if (propertyName.StartsWith(kind, StringComparison.Ordinal) && propertyName.EndsWith("Index", StringComparison.Ordinal)) return kind.ToUpperInvariant();
        return null;
    }

    public static string DirectoryFor(ConfigDocument document)
    {
        var node = document.Settings.FirstOrDefault(x => ConfigDocument.Attribute(x, "Type") == "xyz.Elements.Elements.IoElement");
        var relative = node is null ? "IO" : ConfigDocument.Value(node, "PointTableDirectory") ?? "IO";
        return Path.GetFullPath(relative, Path.GetDirectoryName(document.FilePath)!);
    }

    public static List<IoPointChoice> Read(string directory, string kind)
    {
        var file = Path.Combine(directory, kind + ".csv");
        if (!File.Exists(file)) throw new FileNotFoundException($"未找到 {kind} 点表：{file}。可以另选点表目录。", file);
        using var parser = new TextFieldParser(file, System.Text.Encoding.UTF8) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? [];
        var columns = headers.Select((name, index) => (name, index)).ToDictionary(x => x.name.Trim(), x => x.index, StringComparer.OrdinalIgnoreCase);
        if (!columns.ContainsKey("Index")) throw new InvalidDataException($"{file} 缺少 Index 列。");
        var result = new List<IoPointChoice>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            string Get(string name) => columns.TryGetValue(name, out var column) && column < fields.Length ? fields[column].Trim() : "";
            if (int.TryParse(Get("Index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0)
                result.Add(new(index.ToString(CultureInfo.InvariantCulture), Get("Module"), Get("Component"), Get("Name"), Get("Description")));
        }
        return result;
    }
}
