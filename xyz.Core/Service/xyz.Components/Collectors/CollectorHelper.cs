using System.Globalization;
using System.Reflection;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Collectors;

/// <summary>
/// 组件上的一项变量声明（EC 或 SV）：全名、所在组件、属性、[VariableMark]。
/// </summary>
internal sealed record VariableDeclaration(string Name, ComponentBase Owner, PropertyInfo Property, VariableMarkAttribute Mark);

/// <summary>
/// 采集器共用的扫描与取值。
/// </summary>
internal static class CollectorHelper
{
    /// <summary>
    /// 组件树按装配顺序（先父后子）逐个走一遍。
    /// </summary>
    public static IEnumerable<ComponentBase> Walk(IEnumerable<ComponentBase> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Walk(root.Children))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// 扫组件树上某一类 [VariableMark] 变量：全名 = 组件全路径.属性名（SV 可用 Name 覆盖采集名，EC 不认）。
    /// 属性读不了、全名重复的记日志后跳过。
    /// </summary>
    public static List<VariableDeclaration> ScanVariables(IEnumerable<ComponentBase> roots, VariableType type, string kind)
    {
        var result = new List<VariableDeclaration>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in Walk(roots))
        {
            foreach (var property in component.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var mark = property.GetCustomAttribute<VariableMarkAttribute>();
                if (mark is null || mark.Type != type)
                {
                    continue;
                }

                string local = type == VariableType.SV && !string.IsNullOrWhiteSpace(mark.Name) ? mark.Name : property.Name;
                string name = $"{component.FullPath}.{local}";
                if (property.GetMethod is not { IsPublic: true } || property.GetIndexParameters().Length != 0)
                {
                    LogHelper.Warn(kind, $"{name} 没有可读的公开 get，不编号");
                    continue;
                }

                if (!names.Add(name))
                {
                    LogHelper.Error(kind, $"{name} 重复声明，只认第一个");
                    continue;
                }

                result.Add(new VariableDeclaration(name, component, property, mark));
            }
        }

        return result;
    }

    /// <summary>
    /// 读 [Alarm]、[EventAttribut] 这类"成员值就是代码"的声明：公开实例字符串字段或属性的值；读不出来返回 null。
    /// </summary>
    public static string? ReadCode(MemberInfo member, ComponentBase component)
    {
        return member switch
        {
            FieldInfo field when field.FieldType == typeof(string) => field.GetValue(component) as string,
            PropertyInfo property when property.PropertyType == typeof(string)
                && property.GetMethod?.IsPublic == true
                && property.GetIndexParameters().Length == 0 => property.GetValue(component) as string,
            _ => null
        };
    }

    /// <summary>
    /// 读一项变量的当前值并转成字符串（数字按 InvariantCulture，枚举取名字，null 为空串）；读失败记警告返回空串。
    /// </summary>
    public static string ReadValue(VariableDeclaration declaration, string kind)
    {
        try
        {
            return declaration.Property.GetValue(declaration.Owner) switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                var value => value.ToString() ?? string.Empty
            };
        }
        catch (Exception exception)
        {
            LogHelper.Warn(kind, $"{declaration.Name} 读值失败: {(exception.InnerException ?? exception).Message}");
            return string.Empty;
        }
    }
}
