namespace ToolBuilder.Core;

/// <summary>寻找机型前台资源程序集。部署环境可在元件来源中明确选取该 DLL。</summary>
public static class ClientVisualSourceDiscovery
{
    public static IReadOnlyList<string> Find(string machinePath, IEnumerable<string> sources)
    {
        var files = new List<string>();
        foreach (string source in sources)
        {
            if (File.Exists(source) && IsPresentation(source)) files.Add(source);
            if (!Directory.Exists(source)) continue;
            files.AddRange(Directory.EnumerateFiles(source, "*.Client.Presentation.dll"));
            string modules = Path.Combine(source, "Modules");
            if (Directory.Exists(modules)) files.AddRange(Directory.EnumerateFiles(modules, "*.Client.Presentation.dll", SearchOption.AllDirectories));
        }
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(machinePath))!); directory is not null; directory = directory.Parent)
        {
            string project = Path.Combine(directory.FullName, "Client", "Presentation", "bin");
            if (!Directory.Exists(project)) continue;
            files.AddRange(Directory.EnumerateFiles(project, "*.Client.Presentation.dll", SearchOption.AllDirectories)); break;
        }
        return files.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.Ordinal).First())
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }
    private static bool IsPresentation(string path) => Path.GetFileName(path).EndsWith(".Client.Presentation.dll", StringComparison.OrdinalIgnoreCase);
}
