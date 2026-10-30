using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace xyz._35021.Client.Presentation.Chambers;

/// <summary>35021 的装配规则：实际设备决定清单，布局仅覆盖外观。项目组合方式留在机型中。</summary>
public static class ChamberLayoutComposer
{
    public static string Relative(string path, string root, string module)
    {
        foreach (string prefix in new[] { root, module }.Where(p => p.Length > 0))
            if (path.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)) return path[(prefix.Length + 1)..];
        return path;
    }
    public static ChamberLayoutModel Compose(ChamberDeviceDataDto devices, ChamberLayoutModel? saved)
    {
        string root = saved?.ComponentRoot ?? devices.Module;
        string RelativePath(string path) => Relative(path, root, devices.Module);
        var result = new ChamberLayoutModel { ComponentRoot = root, ChamberType = saved?.ChamberType ?? "" };
        result.Parts.Add(new("Base", "$base"));
        ChamberPartLayout Add(string kind, string path, string parent = "")
        {
            string id = RelativePath(path);
            var part = new ChamberPartLayout(kind, id) { Component = id, ParentId = parent };
            result.Parts.Add(part); return part;
        }
        if (devices.Door is { } door) Add("Door", door.Path).Z = 2.42;
        for (int i = 0; i < devices.Bowls.Count; i++)
        {
            var part = Add("Bowl", devices.Bowls[i].Path);
            part.Sizes["Radius"] = 1.59 + i * 0.18; part.Sizes["HeightScale"] = 1 + i * 0.3;
        }
        if (devices.Spin is { } spin) Add("Disk", spin.Path).Y = 0.32;
        for (int i = 0; i < devices.Arms.Count; i++)
        {
            var arm = devices.Arms[i]; var part = Add("Arm", arm.Path);
            part.X = i % 2 == 0 ? 1.95 : -1.95; part.Z = -1.25 + i / 2 * 0.6;
            if (arm.Lift is { } lift) Add("Lift", lift.Path, part.Id);
            for (int n = 0; n < arm.Nozzles.Count; n++)
            {
                var nozzle = Add("Nozzle", arm.Nozzles[n].Path, part.Id);
                nozzle.Y = 0.07; nozzle.Z = (n - (arm.Nozzles.Count - 1) / 2d) * 0.105;
            }
            if (arm.Nozzles.Count > 0) result.Parts.Add(new("HomeCup", part.Id + ".$cup") { ParentId = part.Id });
        }
        if (saved is not null) foreach (var part in result.Parts)
        {
            var placement = saved.Parts.FirstOrDefault(p => p.Kind == part.Kind
                && (part.Component.Length == 0 ? p.Id == part.Id : RelativePath(p.Component) == part.Component)
                && RelativePath(p.ParentId) == part.ParentId);
            if (placement is not null) part.ApplyPlacement(placement);
        }
        return result;
    }
}
