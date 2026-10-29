using xyz.Client.Presentation.Localization;

namespace ChamberLayoutEditor;

public sealed record ChamberComponentDefinition(string Kind, string NameStem)
{
    public string Title => L10n.Get("chambereditor.kind." + Kind);
}

/// <summary>编辑器支持的原生组件目录；布局只能引用此目录中的类型。</summary>
public static class ChamberComponentCatalog
{
    public static IReadOnlyList<ChamberComponentDefinition> Items { get; } = Array.AsReadOnly(new[]
    {
        new ChamberComponentDefinition("ArmAssembly", "Arm"), new("Lift", "Lift"), new("HomeCup", "Cup"),
        new("Nozzle", "Nozzle"), new("Disk", "SpinMotor"), new("Bowl", "Bowl"), new("Door", "Door"), new("Base", "Base")
    });

    public static bool Contains(string kind) => Items.Any(item => item.Kind == kind);
}
