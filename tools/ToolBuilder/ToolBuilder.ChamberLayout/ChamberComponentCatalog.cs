namespace ChamberLayoutEditor;

/// <summary>编辑器支持的原生组件目录；布局只能引用此目录中的类型。</summary>
public static class ChamberComponentCatalog
{
    public static bool Contains(string kind) => kind is "Arm" or "Lift" or "HomeCup" or "Nozzle" or "Disk" or "Bowl" or "Door" or "Base";
}
