namespace ToolBuilder.Core;

/// <summary>从 machine.xml 的部署目录或所属源码项目查找已编译的元件，不启动设备。</summary>
public static class ElementSourceDiscovery
{
    public static IReadOnlyList<string> Find(string machinePath)
    {
        var sources = new List<string>();
        void Add(string? directory)
        {
            if (directory is not null && Directory.Exists(directory)
                && Directory.EnumerateFiles(directory, "*.dll").Any()
                && !sources.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                sources.Add(directory);
            }
        }

        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(machinePath))!);
        Add(directory.FullName);
        Add(directory.Parent?.FullName);
        bool foundProject = false;
        for (var current = directory; current is not null; current = current.Parent)
        {
            if (!foundProject)
            {
                var project = current.EnumerateFiles("*.csproj").FirstOrDefault();
                if (project is not null)
                {
                    Add(FindOutput(current.FullName, Path.GetFileNameWithoutExtension(project.Name)));
                    foundProject = true;
                }
            }

            // 机型项目只复制自身 DLL；公共依赖由框架后端宿主提供。
            var host = Path.Combine(current.FullName, "xyz.Core", "Service", "xyz.GrpcHost");
            if (Directory.Exists(host))
            {
                Add(FindOutput(host, "xyz.GrpcHost"));
                break;
            }
        }
        return sources;
    }

    private static string? FindOutput(string projectDirectory, string assemblyName)
    {
        string bin = Path.Combine(projectDirectory, "bin");
        if (!Directory.Exists(bin))
        {
            return null;
        }
        return Directory.EnumerateFiles(bin, assemblyName + ".dll", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(bin, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "ref" or "refint" or "Modules"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault();
    }
}
