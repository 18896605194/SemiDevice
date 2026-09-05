using System.Reflection;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Configs;

namespace xyz.Components;

/// <summary>
/// EC 合并器：装配完成后遍历组件树，把 [VariableMark(EC)] 声明合并进 EC 内存树。
/// </summary>
public static class EcMerger
{
    /// <summary>
    /// 合并组件树上全部 EC 声明；返回是否有写盘。
    /// </summary>
    public static bool Merge(IReadOnlyList<ComponentBase> roots)
    {
        EC.Load();

        bool changed = false;
        foreach (var root in roots)
        {
            changed |= MergeComponent(root, parentPath: string.Empty);
        }

        if (changed)
        {
            EC.Flush();
        }

        return changed;
    }

    private static bool MergeComponent(ComponentBase component, string parentPath)
    {
        // 规整 FullPath：构造时挂的子组件（如 RFID）原本只有本级名，这里补全层级路径。
        var path = parentPath.Length == 0 ? component.Name : parentPath + "." + component.Name;
        component.FullPath = path;

        bool changed = false;
        foreach (var property in component.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var mark = property.GetCustomAttribute<VariableMarkAttribute>();
            if (mark is null || mark.Type != VariableType.EC)
            {
                continue;
            }

            changed |= EC.UpsertValueMetadata(
                path, property.Name,
                mark.Default ?? string.Empty,
                mark.Format.ToString(), mark.Unit, mark.Min, mark.Max,
                mark.Default, mark.Description, mark.Options);
        }

        foreach (var child in component.Children)
        {
            changed |= MergeComponent(child, path);
        }

        return changed;
    }
}
