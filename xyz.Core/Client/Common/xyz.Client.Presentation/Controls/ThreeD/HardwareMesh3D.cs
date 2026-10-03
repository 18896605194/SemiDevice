using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>封闭实体网格；统一朝外绕序，静态几何冻结后复用。</summary>
internal static class HardwareMesh3D
{
    /// <summary>绕 Y 轴旋转的一段截面；截面沿外壁向上、内壁向下排列。</summary>
    public static MeshGeometry3D RevolvedBand(double radius0, double y0, double radius1, double y1)
    {
        var mesh = new MeshGeometry3D();
        const int segments = 128;
        for (int i = 0; i < segments; i++)
        {
            double a = i * Math.PI * 2 / segments;
            double b = (i + 1) * Math.PI * 2 / segments;
            Point3D Point(double radius, double y, double angle) => new(radius * Math.Cos(angle), y, radius * Math.Sin(angle));
            Vector3D Normal(double angle)
            {
                var normal = new Vector3D((y1 - y0) * Math.Cos(angle), radius0 - radius1, (y1 - y0) * Math.Sin(angle));
                normal.Normalize();
                return normal;
            }
            var p = Point(radius0, y0, a);
            var q = Point(radius1, y1, a);
            var r = Point(radius1, y1, b);
            var s = Point(radius0, y0, b);
            Triangle(mesh, p, q, r, Normal(a), Normal(a), Normal(b));
            Triangle(mesh, p, r, s, Normal(a), Normal(b), Normal(b));
        }
        mesh.Freeze();
        return mesh;
    }

    public static MeshGeometry3D Annulus(double innerRadius, double outerRadius)
    {
        var mesh = new MeshGeometry3D();
        const int segments = 40;
        for (int i = 0; i < segments; i++)
        {
            double a = i * Math.PI * 2 / segments;
            double b = (i + 1) * Math.PI * 2 / segments;
            Point3D Point(double radius, double angle) => new(radius * Math.Cos(angle), 0, radius * Math.Sin(angle));
            Triangle(mesh, Point(innerRadius, a), Point(outerRadius, b), Point(outerRadius, a));
            Triangle(mesh, Point(innerRadius, a), Point(innerRadius, b), Point(outerRadius, b));
        }
        mesh.Freeze();
        return mesh;
    }

    public static MeshGeometry3D ChamferedBox(Point3D origin, double length, double height, double width)
    {
        double cut = Math.Min(length, width) * 0.12;
        // 从 +Y 看按朝外法线排列；每个侧面保留独立法线，呈现清晰的机械棱边。
        (double X, double Z)[] outline =
        [
            (cut, 0), (0, cut), (0, width - cut), (cut, width),
            (length - cut, width), (length, width - cut), (length, cut), (length - cut, 0)
        ];
        var mesh = new MeshGeometry3D();
        var bottomCenter = origin + new Vector3D(length / 2, 0, width / 2);
        var topCenter = bottomCenter + new Vector3D(0, height, 0);
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            var bottomA = origin + new Vector3D(a.X, 0, a.Z);
            var bottomB = origin + new Vector3D(b.X, 0, b.Z);
            var topA = bottomA + new Vector3D(0, height, 0);
            var topB = bottomB + new Vector3D(0, height, 0);
            Triangle(mesh, topCenter, topA, topB);
            Triangle(mesh, bottomCenter, bottomB, bottomA);
            Triangle(mesh, bottomA, bottomB, topB);
            Triangle(mesh, bottomA, topB, topA);
        }
        mesh.Freeze();
        return mesh;
    }

    public static MeshGeometry3D Cylinder(double radius, double height, double bottom)
    {
        const int segments = 48;
        var mesh = new MeshGeometry3D();
        for (int i = 0; i < segments; i++)
        {
            double a = 2 * Math.PI * i / segments;
            double b = 2 * Math.PI * (i + 1) / segments;
            var normalA = new Vector3D(Math.Cos(a), 0, Math.Sin(a));
            var normalB = new Vector3D(Math.Cos(b), 0, Math.Sin(b));
            var bottomA = new Point3D(radius * normalA.X, bottom, radius * normalA.Z);
            var bottomB = new Point3D(radius * normalB.X, bottom, radius * normalB.Z);
            var topA = bottomA + new Vector3D(0, height, 0);
            var topB = bottomB + new Vector3D(0, height, 0);
            Triangle(mesh, new Point3D(0, bottom + height, 0), topB, topA);
            Triangle(mesh, new Point3D(0, bottom, 0), bottomA, bottomB);
            Triangle(mesh, bottomA, topA, topB, normalA, normalA, normalB);
            Triangle(mesh, bottomA, topB, bottomB, normalA, normalB, normalB);
        }
        mesh.Freeze();
        return mesh;
    }

    private static void Triangle(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c)
    {
        Vector3D normal = Vector3D.CrossProduct(b - a, c - a);
        normal.Normalize();
        Triangle(mesh, a, b, c, normal, normal, normal);
    }

    private static void Triangle(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c,
        Vector3D normalA, Vector3D normalB, Vector3D normalC)
    {
        int start = mesh.Positions.Count;
        mesh.Positions.Add(a);
        mesh.Positions.Add(b);
        mesh.Positions.Add(c);
        mesh.Normals.Add(normalA);
        mesh.Normals.Add(normalB);
        mesh.Normals.Add(normalC);
        mesh.TriangleIndices.Add(start);
        mesh.TriangleIndices.Add(start + 1);
        mesh.TriangleIndices.Add(start + 2);
    }
}
