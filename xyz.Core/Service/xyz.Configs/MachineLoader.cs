using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using xyz.Tools;
using xyz.Configs.Models;

namespace xyz.Configs;

/// <summary>
/// machine.xml 配置文件读取器
/// </summary>
public static class MachineLoader
{
    #region 找文件

    /// <summary>
    /// 配置目录（machine.xml 所在目录）；ec.xml 和各编号表（EcDefinitions.xml 等）都跟它放一起。
    /// </summary>
    public static string ConfigDirectory => Path.GetDirectoryName(FindConfigFile())!;

    /// <summary>
    /// 找 machine.xml：只认运行目录 Paths.json 里 ConfigPath 指的目录
    /// </summary>
    private static string FindConfigFile()
    {
        string pathsFile = Path.Combine(AppContext.BaseDirectory, "Paths.json");
        if (!File.Exists(pathsFile))
        {
            throw new FileNotFoundException($"运行目录没有 Paths.json（machine.xml 在哪儿由它指）：{pathsFile}");
        }

        string? configPath = null;
        using (var document = JsonDocument.Parse(File.ReadAllText(pathsFile)))
        {
            if (document.RootElement.TryGetProperty("ConfigPath", out var value))
            {
                configPath = value.GetString();
            }
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new InvalidOperationException($"{pathsFile} 里没配 ConfigPath（machine.xml 所在目录）");
        }

        string directory = Path.GetFullPath(configPath, AppContext.BaseDirectory);
        string machineFile = Path.Combine(directory, "machine.xml");
        if (!File.Exists(machineFile))
        {
            throw new FileNotFoundException($"配置目录未找到 machine.xml：{directory}");
        }

        return machineFile;
    }

    #endregion

    #region 读配置树

    public static IReadOnlyList<ModuleConfig> Load()
    {
        var path = FindConfigFile(); //寻找文件地址
        var config = XmlHelper.Deserialize<MachineSettings>(path);//映射到MachineSettings
        return config?.Modules ?? [];
    }

    #endregion

    #region 写回一个参数

    /// <summary>
    /// 正常不会调用 目前只有改变wafer尺寸会调用
    /// </summary>
    /// <param name="settingPath"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    /// <exception cref="InvalidOperationException"></exception>
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

    #endregion
}
