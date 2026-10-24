using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using xyz.Tools;
using xyz.Configs.Models;

namespace xyz.Configs;

/// <summary>
/// machine.xml 配置文件读取器（先解析模块级 Setting 节点）。
/// </summary>
public static class MachineXml
{
    public static IReadOnlyList<ModuleConfig> Load()
    {
        var path = FindConfigFile();
        var config = XmlHelper.Deserialize<MachineSettings>(path);
        return config?.Modules ?? [];
    }

    /// <summary>
    /// 配置目录（machine.xml 所在目录）；ec.xml 和各编号表（EcDefinitions.xml 等）都跟它放一起。
    /// </summary>
    public static string ConfigDirectory => Path.GetDirectoryName(FindConfigFile())!;

    /// <summary>
    /// 把一个参数写回 machine.xml（界面上改、改完一直生效的，现在只有 System 的晶圆尺寸）：settingPath 是组件全路径（"System"、"Chamber.Chamber1"，
    /// 分组节点的名字也在里面），按它一层层找到 Setting，改下面同名的 Value（不分大小写），没有就照上一行的缩进加一行。
    /// 别的内容、缩进、换行原样不动；先写临时文件再换上去，写到一半出事不会把 machine.xml 写坏。找不到节点、写不了文件就抛，调用方转成错误码。
    /// </summary>
    public static void SaveValue(string settingPath, string name, string value)
    {
        string file = FindConfigFile();
        string text = File.ReadAllText(file);
        var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException($"{file} 是空的");

        XElement? node = null;
        IEnumerable<XElement> level = root.Elements("Setting");
        foreach (string segment in settingPath.Split('.'))
        {
            node = level.FirstOrDefault(element => string.Equals((string?)element.Attribute("Name"), segment, StringComparison.OrdinalIgnoreCase));
            if (node is null)
            {
                throw new InvalidOperationException($"machine.xml 里找不到节点 {settingPath}");
            }

            level = node.Elements("Setting");
        }

        if (node is null)
        {
            throw new InvalidOperationException("没给 machine.xml 的节点路径");
        }

        var existing = node.Elements("Value")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.SetAttributeValue("Value", value);
        }
        else
        {
            AddValue(node, new XElement("Value", new XAttribute("Name", name), new XAttribute("Value", value)));
        }

        // 解析时 CRLF 都变成了 LF，写回时换回原文件用的那种（仓库里是 LF，git 签出、现场编辑过的可能是 CRLF）；
        // 编码跟源文件一样是带 BOM 的 UTF-8
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(true),
            NewLineHandling = NewLineHandling.Replace,
            NewLineChars = text.Contains("\r\n") ? "\r\n" : "\n",
            OmitXmlDeclaration = document.Declaration is null,
        };
        string temp = file + ".tmp";
        using (var writer = XmlWriter.Create(temp, settings))
        {
            document.Save(writer);
        }

        File.Move(temp, file, overwrite: true);
    }

    /// <summary>
    /// 节点下加一行 Value：有别的 Value 就跟在最后一个后面、用它前面那段缩进；一个都没有就放在最前面，缩进比节点多一个 Tab。
    /// </summary>
    private static void AddValue(XElement node, XElement value)
    {
        var last = node.Elements("Value").LastOrDefault();
        if (last is not null)
        {
            string indent = last.PreviousNode is XText lastIndent ? lastIndent.Value : string.Empty;
            last.AddAfterSelf(new XText(indent), value);
            return;
        }

        string nodeIndent = node.PreviousNode is XText text ? text.Value : "\n";
        if (node.FirstNode is null)
        {
            node.Add(new XText(nodeIndent + "\t"), value, new XText(nodeIndent));
            return;
        }

        node.AddFirst(new XText(nodeIndent + "\t"), value);
    }

    private static string FindConfigFile()
    {
        var pathsFile = Path.Combine(AppContext.BaseDirectory, "Paths.json");
        if (File.Exists(pathsFile))
        {
            var configPath = ReadConfigPath();
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new InvalidOperationException("Paths.json 中未配置 ConfigPath");
            }

            var fullPath = Path.GetFullPath(configPath, AppContext.BaseDirectory);
            var machineFile = Path.Combine(fullPath, "machine.xml");
            if (!File.Exists(machineFile))
            {
                throw new FileNotFoundException($"配置目录未找到 machine.xml: {fullPath}");
            }

            return machineFile;
        }

        // 没有 Paths.json 时的兼容查找
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "machine.xml"),
            Path.Combine(AppContext.BaseDirectory, "Config", "machine.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "machine.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "Config", "machine.xml")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("未找到 machine.xml 配置文件");
    }

    private static string? ReadConfigPath()
    {
        var pathsFile = Path.Combine(AppContext.BaseDirectory, "Paths.json");
        if (!File.Exists(pathsFile))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(pathsFile));
        return document.RootElement.TryGetProperty("ConfigPath", out var value)
            ? value.GetString()
            : null;
    }
}
