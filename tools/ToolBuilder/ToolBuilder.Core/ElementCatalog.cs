using System.Reflection;

namespace ToolBuilder.Core;

public sealed record PropertySpec(string Name, string Kind, string DefaultValue, string Group,
    string Description, bool Required, IReadOnlyList<string> Choices)
{
    public string NormalizeValue(string value)
    {
        if (Kind == "System.Boolean")
        {
            value = value.Trim();
            if (bool.TryParse(value, out var flag)) return flag ? "true" : "false";
        }
        return value;
    }
}

public sealed record ElementSpec(string Key, string TypeName, string Description, string AssemblyPath,
    IReadOnlyList<PropertySpec> Properties, IReadOnlyList<string>? BaseTypes = null, IReadOnlyList<string>? Interfaces = null)
{
    public string Display => $"{TypeName.Split('.').Last()}  ·  {Description}";
    public string ShortName => TypeName.Split('.').Last();
    public bool IsRobot => BaseTypes?.Contains("xyz.Modules.BaseRobotModule") == true;
    public string Category => "元件";
    public string? ChamberRole => Interfaces?.Contains("xyz.Modules.IChamber") == true ? "Chamber"
        : Interfaces?.Contains("xyz.Elements.Interfaces.ISwingArm") == true ? "Arm"
        : Interfaces?.Contains("xyz.Elements.Interfaces.ISpinMotor") == true ? "Disk"
        : Interfaces?.Contains("xyz.Elements.Interfaces.IChamberBowl") == true ? "Bowl"
        : Interfaces?.Contains("xyz.Elements.Interfaces.IChamberDoor") == true ? "Door"
        : Interfaces?.Contains("xyz.Elements.Interfaces.IChamberLift") == true ? "Lift"
        : Interfaces?.Contains("xyz.Elements.Interfaces.INozzle") == true ? "Nozzle" : null;
}

/// <summary>仅解析程序集元数据，不执行元件构造函数、静态初始化或设备连接。</summary>
public sealed class ElementCatalog
{
    public Dictionary<string, ElementSpec> Elements { get; } = new(StringComparer.Ordinal);
    public List<string> Diagnostics { get; } = [];

    public static ElementCatalog Read(IEnumerable<string> sources)
    {
        var result = new ElementCatalog();
        var candidates = new List<string>();
        foreach (var source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(source)) candidates.Add(Path.GetFullPath(source));
                else if (Directory.Exists(source))
                {
                    candidates.AddRange(Directory.EnumerateFiles(source, "*.dll"));
                    var modules = Path.Combine(source, "Modules");
                    if (Directory.Exists(modules)) candidates.AddRange(Directory.EnumerateFiles(modules, "*.dll", SearchOption.AllDirectories));
                }
                else result.Diagnostics.Add($"元件来源不存在：{source}");
            }
            catch (Exception e) { result.Diagnostics.Add($"读取来源失败：{source}：{e.Message}"); }
        }

        // 用户明确选择的文件优先，兄弟 DLL 仅用于解析依赖。
        var dependencies = candidates.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(dir => Directory.EnumerateFiles(dir!, "*.dll"));
        var runtime = Directory.EnumerateFiles(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
        // Modules 中可能带有旧平台 DLL 副本；同一程序集身份统一使用首个来源，
        // 避免元数据解析时把主目录的新版本与模块依赖的旧版本混用。
        var assemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidates.Concat(dependencies).Concat(runtime).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { assemblies.TryAdd(AssemblyName.GetAssemblyName(path).FullName, path); }
            catch (BadImageFormatException) { }
            catch (Exception e) { result.Diagnostics.Add($"读取程序集身份失败：{Path.GetFileName(path)}：{e.Message}"); }
        }
        var paths = assemblies.Values.ToArray();
        var canonicalPaths = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (candidates.Count == 0) return result;
        using var context = new MetadataLoadContext(new PathAssemblyResolver(paths), "System.Private.CoreLib");
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(canonicalPaths.Contains))
        {
            try
            {
                var assembly = context.LoadFromAssemblyPath(path);
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.OfType<Type>().ToArray();
                    result.Diagnostics.Add($"{Path.GetFileName(path)} 部分类型无法解析，请补选依赖目录：{e.LoaderExceptions.FirstOrDefault()?.Message}");
                }
                foreach (var type in types)
                {
                    try
                    {
                        var attribute = type.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.FullName == "xyz.Elements.Attributes.ElementAttribute");
                        if (attribute is null || type.IsAbstract || !IsElement(type)) continue;
                        var key = Text(attribute, 0);
                        if (string.IsNullOrWhiteSpace(key)) key = type.FullName!;
                        var properties = new List<PropertySpec>();
                        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            var editor = property.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.FullName == "xyz.Elements.Attributes.MachineAttribute");
                            if (editor is null || !property.CanWrite) continue;
                            var kind = property.PropertyType.FullName ?? property.PropertyType.Name;
                            var choices = property.PropertyType.IsEnum
                                ? property.PropertyType.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => f.Name).ToArray()
                                : kind == "System.Boolean" ? new[] { "true", "false" } : [];
                            var propertySpec = new PropertySpec(property.Name, kind, Text(editor, 0), Text(editor, 1), Text(editor, 2),
                                editor.NamedArguments.Any(a => a.MemberName == "Required" && a.TypedValue.Value is true), choices);
                            properties.Add(propertySpec with { DefaultValue = propertySpec.NormalizeValue(propertySpec.DefaultValue) });
                        }
                        var baseTypes = new List<string>();
                        for (var current = type.BaseType; current is not null; current = current.BaseType) baseTypes.Add(current.FullName!);
                        var interfaces = type.GetInterfaces().Select(contract => contract.FullName!).ToArray();
                        var spec = new ElementSpec(key, type.FullName!, Text(attribute, 1), assembly.Location, properties, baseTypes, interfaces);
                        if (result.Elements.TryGetValue(key, out var previous))
                        {
                            if (previous.TypeName != spec.TypeName)
                                result.Diagnostics.Add($"重复元件键 {key}，使用 {previous.AssemblyPath}，忽略 {path}");
                            continue;
                        }
                        result.Elements.Add(key, spec);
                    }
                    catch (Exception e) { result.Diagnostics.Add($"{type.FullName} 元数据解析失败：{e.Message}"); }
                }
            }
            catch (BadImageFormatException) { /* 本机 DLL 不属于元件目录。 */ }
            catch (Exception e) { result.Diagnostics.Add($"{Path.GetFileName(path)} 加载失败：{e.Message}"); }
        }
        return result;
    }

    private static bool IsElement(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (current.FullName == "xyz.Elements.ElementBase") return true;
        return false;
    }

    private static string Text(CustomAttributeData attribute, int index) =>
        index < attribute.ConstructorArguments.Count ? attribute.ConstructorArguments[index].Value?.ToString() ?? "" : "";
}
