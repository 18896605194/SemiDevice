using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ToolBuilder.Core;

/// <summary>直接编辑 XML，保留注释、未知属性和未加载元件的数据。</summary>
public sealed class ConfigDocument
{
    public XDocument Xml { get; }
    public string FilePath { get; private set; }
    private byte[] _fingerprint;
    public bool IsDirty { get; private set; }
    public event Action? Changed;
    public string OriginalText { get; private set; }
    /// <summary>原文件的换行符（\r\n 或 \n）。读 XML 时换行都规整成了 \n，保存时换回这个，免得整份文件的换行被悄悄改掉。</summary>
    public string NewLine { get; }

    private ConfigDocument(string path, XDocument xml, byte[] bytes)
    {
        FilePath = path;
        Xml = xml;
        _fingerprint = SHA256.HashData(bytes);
        OriginalText = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        NewLine = OriginalText.Contains("\r\n") ? "\r\n" : "\n";
        Xml.Changed += (_, _) => { IsDirty = true; Changed?.Invoke(); };
    }

    public static ConfigDocument Open(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        if (xml.Root?.Name != "ArrayOfSetting") throw new InvalidDataException("machine.xml 根节点必须是 ArrayOfSetting。");
        var document = new ConfigDocument(Path.GetFullPath(path), xml, bytes);
        RenameImpact.Remember(document);  // 记下原名，改名时提示连带影响
        return document;
    }

    public IEnumerable<XElement> Settings => Xml.Descendants("Setting");

    public string GetText() => Xml.Declaration is null ? Xml.ToString(SaveOptions.DisableFormatting)
        : Xml.Declaration + Environment.NewLine + Xml.ToString(SaveOptions.DisableFormatting);

    /// <summary>覆盖保存前的备份放在目标文件同级的这个子目录里，不跟 machine.xml 混在一起，也不会被一起提交进仓库。</summary>
    public const string BackupFolder = "backup";

    public FileWrite PrepareWrite(string target)
    {
        target = Path.GetFullPath(target);
        byte[]? expected = string.Equals(target, FilePath, StringComparison.OrdinalIgnoreCase) ? _fingerprint
            : File.Exists(target) ? SHA256.HashData(File.ReadAllBytes(target)) : null;
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(true), Indent = false, NewLineHandling = NewLineHandling.Replace,
            NewLineChars = NewLine, CloseOutput = false
        }))
        {
            Xml.Save(writer);
        }
        return new FileWrite(target, stream.ToArray(), expected);
    }

    public void AcceptSave(FileWrite write)
    {
        FilePath = Path.GetFullPath(write.Path);
        _fingerprint = SHA256.HashData(write.Content);
        OriginalText = Encoding.UTF8.GetString(write.Content).TrimStart('\uFEFF');
        IsDirty = false;
        RenameImpact.Remember(this);
        Changed?.Invoke();
    }

    public string? Save(string target)
    {
        target = Path.GetFullPath(target);
        if (string.Equals(target, FilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(target)
            && !SHA256.HashData(File.ReadAllBytes(target)).SequenceEqual(_fingerprint))
            throw new IOException("文件已被其他程序修改。请重新打开，或另存为新文件，避免覆盖外部修改。");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = Path.Combine(Path.GetDirectoryName(target)!, $".scedit-{Guid.NewGuid():N}.tmp");
        string? backup = null;
        try
        {
            using (var writer = XmlWriter.Create(temp, new XmlWriterSettings { Encoding = new UTF8Encoding(true), Indent = false,
                       NewLineHandling = NewLineHandling.Replace, NewLineChars = NewLine }))
                Xml.Save(writer);
            if (File.Exists(target))
            {
                var backupDirectory = Path.Combine(Path.GetDirectoryName(target)!, BackupFolder);
                Directory.CreateDirectory(backupDirectory);
                backup = Path.Combine(backupDirectory, Path.GetFileName(target) + "."
                    + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "." + Guid.NewGuid().ToString("N")[..6] + ".bak");
                File.Replace(temp, target, backup);
            }
            else File.Move(temp, target);
            FilePath = target;
            var bytes = File.ReadAllBytes(target);
            _fingerprint = SHA256.HashData(bytes);
            OriginalText = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            IsDirty = false;
            RenameImpact.Remember(this);  // \u5B58\u4E0B\u53BB\u7684\u540D\u5B57\u5C31\u662F\u65B0\u7684\u539F\u540D\uFF0C\u540C\u4E00\u6B21\u6539\u540D\u4E0D\u518D\u91CD\u590D\u63D0\u793A
            Changed?.Invoke();
            return backup;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static string Attribute(XElement node, string name) => (string?)node.Attribute(name) ?? "";
    public static string? Value(XElement node, string name) => node.Elements("Value")
        .FirstOrDefault(v => string.Equals(Attribute(v, "Name"), name, StringComparison.OrdinalIgnoreCase))?.Attribute("Value")?.Value;

    public static void SetAttribute(XElement node, string name, string? value)
    {
        if (Attribute(node, name) == (value ?? "")) return;
        node.SetAttributeValue(name, string.IsNullOrEmpty(value) ? null : value);
    }

    public static void SetValue(XElement node, string name, string value)
    {
        var existing = node.Elements("Value").FirstOrDefault(v => string.Equals(Attribute(v, "Name"), name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if ((string?)existing.Attribute("Value") != value) existing.SetAttributeValue("Value", value);
        }
        else
        {
            var item = new XElement("Value", new XAttribute("Name", name), new XAttribute("Value", value));
            var lastValue = node.Elements("Value").LastOrDefault();
            if (node.Document is null)
            {
                // 还没挂进文件的新节点：先按顺序放，挂进去时由 XmlLayout 统一排版。
                if (lastValue is not null) lastValue.AddAfterSelf(item); else node.AddFirst(item);
            }
            // 参数排在已有参数后面；还没有参数就排在子节点、注释前面，各占一行。
            else if (lastValue is not null) XmlLayout.AddAfter(lastValue, item);
            else XmlLayout.AddFirst(node, item);
        }
    }

    public static string PathOf(XElement node) => string.Join(".", node.AncestorsAndSelf("Setting").Reverse().Select(x => Attribute(x, "Name")));
}
