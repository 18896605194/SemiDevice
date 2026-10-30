using System.Reflection;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>编辑器和机型使用同一组前台控件提供器。只接收前台资源程序集，不扫描后台元件类型。</summary>
public static class ChamberVisualProviderLoader
{
    public static IReadOnlyList<IChamberVisualProvider> FromAssembly(Assembly assembly) => assembly.GetTypes()
        .Where(type => !type.IsAbstract && !type.IsInterface && typeof(IChamberVisualProvider).IsAssignableFrom(type))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .Select(type => (IChamberVisualProvider)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Cannot create visual provider {type.FullName}."))).ToArray();

    public static IReadOnlyList<IChamberVisualProvider> Load(IEnumerable<string> files)
    {
        var result = new List<IChamberVisualProvider>();
        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!System.IO.Path.GetFileName(file).EndsWith(".Client.Presentation.dll", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Expected a client presentation assembly.", nameof(files));
            result.AddRange(FromAssembly(Assembly.LoadFrom(file)));
        }
        return result;
    }
}
