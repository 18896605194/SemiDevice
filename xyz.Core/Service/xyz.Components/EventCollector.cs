using System.Reflection;
using xyz.Components.Attributes;
using xyz.Components.Events;

namespace xyz.Components;

/// <summary>
/// 采集组件树上的 EventAttribut 声明，返回事件目录。
/// </summary>
public static class EventCollector
{
    /// <summary>
    /// 在组件装配完成、FullPath 已确定后调用，包含子组件及继承的事件声明。
    /// 每次调用独立采集，不修改组件、不注册或发布事件。
    /// </summary>
    public static IReadOnlyList<EventDefinition> Collect(IReadOnlyList<ComponentBase> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var definitions = new List<EventDefinition>();
        var keys = new HashSet<(string SourcePath, string EventCode)>();
        foreach (var root in roots)
        {
            CollectComponent(root, definitions, keys);
        }

        return definitions.AsReadOnly();
    }

    private static void CollectComponent(ComponentBase component, List<EventDefinition> definitions,
        HashSet<(string SourcePath, string EventCode)> keys)
    {
        ArgumentNullException.ThrowIfNull(component);

        foreach (var member in component.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = member.GetCustomAttribute<EventAttribut>(inherit: true);
            if (attribute is null)
            {
                continue;
            }

            var sourcePath = component.FullPath;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new InvalidOperationException($"组件 {component.GetType().Name} 的完整路径尚未确定，无法采集事件。");
            }

            var eventCode = ReadEventCode(member, component);
            if (string.IsNullOrWhiteSpace(eventCode))
            {
                throw new InvalidOperationException($"{sourcePath}.{member.Name} 的事件代码必须是可读取的非空字符串。");
            }

            if (!keys.Add((sourcePath, eventCode)))
            {
                throw new InvalidOperationException($"事件 {sourcePath}.{eventCode} 重复声明。");
            }

            definitions.Add(new EventDefinition
            {
                SourcePath = sourcePath,
                EventCode = eventCode,
                EventText = attribute.EventText,
                Description = attribute.Description
            });
        }

        foreach (var child in component.Children)
        {
            CollectComponent(child, definitions, keys);
        }
    }

    private static string? ReadEventCode(MemberInfo member, ComponentBase component)
    {
        switch (member)
        {
            case FieldInfo field:
                if (field.FieldType != typeof(string))
                {
                    return null;
                }

                return field.GetValue(component) as string;

            case PropertyInfo property:
                if (property.PropertyType != typeof(string))
                {
                    return null;
                }

                if (property.GetMethod is null)
                {
                    return null;
                }

                if (!property.GetMethod.IsPublic)
                {
                    return null;
                }

                if (property.GetIndexParameters().Length != 0)
                {
                    return null;
                }

                return property.GetValue(component) as string;

            default:
                return null;
        }
    }
}
