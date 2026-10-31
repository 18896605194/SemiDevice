using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.Fluid;

/// <summary>流体硬件的封闭实体和贯通接头。</summary>
internal static class PreviewMesh3D
{
    public static MeshGeometry3D Tube(Point3D start, Point3D end, double outer, double inner = 0, int sides = 48)
    {
        var mesh = new MeshGeometry3D(); var axis = end - start; axis.Normalize();
        var u = Vector3D.CrossProduct(axis, Math.Abs(axis.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0)); u.Normalize();
        var v = Vector3D.CrossProduct(axis, u);
        for (int i = 0; i < sides; i++)
        {
            Vector3D Radial(int index) { double angle = index * Math.Tau / sides; return u * Math.Cos(angle) + v * Math.Sin(angle); }
            var r0 = Radial(i); var r1 = Radial(i + 1);
            var a = start + r0 * outer; var b = start + r1 * outer; var c = end + r0 * outer; var d = end + r1 * outer;
            Quad(mesh, a, b, d, c);
            if (inner > 0)
            {
                var ai = start + r0 * inner; var bi = start + r1 * inner; var ci = end + r0 * inner; var di = end + r1 * inner;
                Quad(mesh, ai, ci, di, bi); Quad(mesh, a, ai, bi, b); Quad(mesh, c, d, di, ci);
            }
            else { Triangle(mesh, a, start, b); Triangle(mesh, c, d, end); }
        }
        mesh.Freeze(); return mesh;
    }
    public static MeshGeometry3D Box(Point3D origin, double length, double height, double width, double bevel = 0.06)
    {
        double cut = Math.Min(bevel, Math.Min(length, width) * 0.25);
        (double X, double Z)[] points = [(cut, 0), (0, cut), (0, width - cut), (cut, width), (length - cut, width), (length, width - cut), (length, cut), (length - cut, 0)];
        var mesh = new MeshGeometry3D(); var center = origin + new Vector3D(length / 2, 0, width / 2);
        for (int i = 0; i < points.Length; i++)
        {
            var a = origin + new Vector3D(points[i].X, 0, points[i].Z);
            var b = origin + new Vector3D(points[(i + 1) % points.Length].X, 0, points[(i + 1) % points.Length].Z);
            var topA = a + new Vector3D(0, height, 0); var topB = b + new Vector3D(0, height, 0);
            Triangle(mesh, center + new Vector3D(0, height, 0), topA, topB); Triangle(mesh, center, b, a); Quad(mesh, a, b, topB, topA);
        }
        mesh.Freeze(); return mesh;
    }
    public static MeshGeometry3D Coil(double startX, double endX, double y, double radius, double wireRadius, int turns)
    {
        int rings = turns * 20, sides = 10; var mesh = new MeshGeometry3D();
        Point3D Center(int i) { double t = (double)i / rings, angle = t * turns * Math.Tau; return new Point3D(startX + (endX - startX) * t, y + radius * Math.Cos(angle), radius * Math.Sin(angle)); }
        (Vector3D U, Vector3D V) Frame(int i)
        {
            var tangent = Center(Math.Min(rings, i + 1)) - Center(Math.Max(0, i - 1)); tangent.Normalize();
            var u = Vector3D.CrossProduct(tangent, new Vector3D(1, 0, 0)); u.Normalize(); return (u, Vector3D.CrossProduct(tangent, u));
        }
        for (int i = 0; i < rings; i++)
        {
            var a = Center(i); var b = Center(i + 1); var fa = Frame(i); var fb = Frame(i + 1);
            for (int j = 0; j < sides; j++)
            {
                double t0 = j * Math.Tau / sides, t1 = (j + 1) * Math.Tau / sides;
                Point3D Around(Point3D p, (Vector3D U, Vector3D V) f, double angle) => p + (f.U * Math.Cos(angle) + f.V * Math.Sin(angle)) * wireRadius;
                Quad(mesh, Around(a, fa, t0), Around(a, fa, t1), Around(b, fb, t1), Around(b, fb, t0));
            }
        }
        mesh.Freeze(); return mesh;
    }
    private static void Quad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d) { Triangle(mesh, a, b, c); Triangle(mesh, a, c, d); }
    private static void Triangle(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c)
    {
        var normal = Vector3D.CrossProduct(b - a, c - a); normal.Normalize(); int index = mesh.Positions.Count;
        mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c); mesh.Normals.Add(normal); mesh.Normals.Add(normal); mesh.Normals.Add(normal);
        mesh.TriangleIndices.Add(index); mesh.TriangleIndices.Add(index + 1); mesh.TriangleIndices.Add(index + 2);
    }
}
