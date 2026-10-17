using System.Windows;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 沿三维路径生成管壁、液芯和端口网格。
/// 纹理坐标使用累计路径长度，使水纹通过弯头时保持连续。
/// </summary>
internal sealed class FluidRouteGeometry
{
    private const int CircleSegments = 32;
    private const double PointTolerance = 0.0000001;
    private const double StraightStep = 0.08;

    private readonly List<Point3D> _points = new();
    private readonly List<Vector3D> _horizontalAxes = new();
    private readonly List<Vector3D> _verticalAxes = new();
    private readonly List<Vector3D> _tangents = new();
    private readonly List<double> _distances = new();

    public bool IsEmpty
    {
        get
        {
            return _points.Count < 2;
        }
    }

    public FluidRouteGeometry(Point3DCollection? path, double bendRadius)
    {
        List<Point3D> corners = GetValidCorners(path);
        if (corners.Count < 2)
        {
            return;
        }

        BuildCenterLine(corners, bendRadius);
        BuildCrossSectionAxes();
    }

    private static List<Point3D> GetValidCorners(Point3DCollection? path)
    {
        var corners = new List<Point3D>();
        if (path == null)
        {
            return corners;
        }

        foreach (Point3D point in path)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
            {
                // 任一点无效时整条路径不显示，避免把剩余点误连成另一条管路。
                corners.Clear();
                return corners;
            }

            if (corners.Count == 0 || (point - corners[corners.Count - 1]).Length > PointTolerance)
            {
                corners.Add(point);
            }
        }

        return corners;
    }

    private void BuildCenterLine(List<Point3D> corners, double bendRadius)
    {
        Point3D previous = corners[0];
        for (int index = 1; index < corners.Count - 1; index++)
        {
            Point3D corner = corners[index];
            Vector3D incomingSegment = corner - corners[index - 1];
            Vector3D outgoingSegment = corners[index + 1] - corner;
            Vector3D incoming = Normalize(incomingSegment);
            Vector3D outgoing = Normalize(outgoingSegment);
            double cosine = Math.Clamp(Vector3D.DotProduct(incoming, outgoing), -1, 1);

            if (bendRadius == 0 || Math.Abs(cosine) > 0.9999)
            {
                AddStraightSegment(previous, corner);
                previous = corner;
                continue;
            }

            double angle = Math.Acos(cosine);
            double halfAngleTangent = Math.Tan(angle / 2);

            // 每端最多占相邻直管的 45%，避免两个弯头在短管段上互相覆盖。
            double availableLength = Math.Min(incomingSegment.Length, outgoingSegment.Length) * 0.45;
            double trimLength = Math.Min(bendRadius * halfAngleTangent, availableLength);
            double actualRadius = trimLength / halfAngleTangent;
            Vector3D rotationAxis = Normalize(Vector3D.CrossProduct(incoming, outgoing));
            Point3D arcStart = corner - incoming * trimLength;
            Point3D arcCenter = arcStart + Vector3D.CrossProduct(rotationAxis, incoming) * actualRadius;
            Vector3D radial = arcStart - arcCenter;

            AddStraightSegment(previous, arcStart);

            int arcSegments = Math.Clamp((int)Math.Ceiling(angle * 12), 4, 40);
            for (int step = 0; step < arcSegments; step++)
            {
                double stepAngle = angle * step / arcSegments;
                Vector3D rotatedRadial = radial * Math.Cos(stepAngle)
                    + Vector3D.CrossProduct(rotationAxis, radial) * Math.Sin(stepAngle);
                AddPoint(arcCenter + rotatedRadial);
            }

            previous = corner + outgoing * trimLength;
        }

        Point3D lastPoint = corners[corners.Count - 1];
        AddStraightSegment(previous, lastPoint);
        AddPoint(lastPoint);
    }

    private void BuildCrossSectionAxes()
    {
        for (int index = 0; index < _points.Count; index++)
        {
            int previousIndex = Math.Max(0, index - 1);
            int nextIndex = Math.Min(index + 1, _points.Count - 1);
            Vector3D tangent = Normalize(_points[nextIndex] - _points[previousIndex]);

            if (tangent.LengthSquared < 0.000000000001)
            {
                if (index > 0)
                {
                    tangent = _tangents[index - 1];
                }
                else
                {
                    tangent = new Vector3D(1, 0, 0);
                }
            }

            // 沿用上一个截面的方向，减少空间弯管处的纹理扭转。
            Vector3D horizontal = new Vector3D();
            if (index > 0)
            {
                Vector3D previousHorizontal = _horizontalAxes[index - 1];
                horizontal = previousHorizontal - tangent * Vector3D.DotProduct(previousHorizontal, tangent);
            }

            if (horizontal.LengthSquared < 0.000000000001)
            {
                Vector3D referenceAxis = new Vector3D(0, 1, 0);
                if (Math.Abs(tangent.Y) > 0.85)
                {
                    referenceAxis = new Vector3D(0, 0, 1);
                }

                horizontal = Vector3D.CrossProduct(tangent, referenceAxis);
            }

            horizontal = Normalize(horizontal);
            _horizontalAxes.Add(horizontal);
            _verticalAxes.Add(Vector3D.CrossProduct(tangent, horizontal));
            _tangents.Add(tangent);

            double distance = 0;
            if (index > 0)
            {
                distance = _distances[index - 1] + (_points[index] - _points[index - 1]).Length;
            }

            _distances.Add(distance);
        }
    }

    public MeshGeometry3D CreateTube(double radius, bool inward = false, bool caps = false)
    {
        var mesh = new MeshGeometry3D();
        if (IsEmpty)
        {
            mesh.Freeze();
            return mesh;
        }

        for (int pointIndex = 0; pointIndex < _points.Count; pointIndex++)
        {
            for (int side = 0; side <= CircleSegments; side++)
            {
                Vector3D radial = GetRadialDirection(pointIndex, side);
                mesh.Positions.Add(_points[pointIndex] + radial * radius);

                Vector3D normal = radial;
                if (inward)
                {
                    normal = -normal;
                }

                mesh.Normals.Add(normal);
                mesh.TextureCoordinates.Add(new Point(_distances[pointIndex], (double)side / CircleSegments));
            }
        }

        for (int pointIndex = 0; pointIndex < _points.Count - 1; pointIndex++)
        {
            for (int side = 0; side < CircleSegments; side++)
            {
                int current = pointIndex * (CircleSegments + 1) + side;
                int next = current + CircleSegments + 1;
                AddQuad(mesh, current, current + 1, next + 1, next, inward);
            }
        }

        if (caps)
        {
            AddCap(mesh, 0, radius);
            AddCap(mesh, _points.Count - 1, radius);
        }

        mesh.Freeze();
        return mesh;
    }

    private void AddCap(MeshGeometry3D mesh, int pointIndex, double radius)
    {
        Vector3D normal = _tangents[pointIndex];
        if (pointIndex == 0)
        {
            normal = -normal;
        }

        for (int side = 0; side < CircleSegments; side++)
        {
            int first = mesh.Positions.Count;
            AddCapVertex(mesh, _points[pointIndex], normal, _distances[pointIndex]);
            AddCapVertex(mesh, _points[pointIndex] + GetRadialDirection(pointIndex, side) * radius, normal, _distances[pointIndex]);
            AddCapVertex(mesh, _points[pointIndex] + GetRadialDirection(pointIndex, side + 1) * radius, normal, _distances[pointIndex]);

            if (pointIndex == 0)
            {
                AddTriangle(mesh, first, first + 2, first + 1);
            }
            else
            {
                AddTriangle(mesh, first, first + 1, first + 2);
            }
        }
    }

    private static void AddCapVertex(MeshGeometry3D mesh, Point3D point, Vector3D normal, double distance)
    {
        mesh.Positions.Add(point);
        mesh.Normals.Add(normal);
        mesh.TextureCoordinates.Add(new Point(distance, 0.5));
    }

    public MeshGeometry3D CreateRims(double outerRadius, double innerRadius)
    {
        var mesh = new MeshGeometry3D();
        if (!IsEmpty)
        {
            AddRim(mesh, 0, outerRadius, innerRadius);
            AddRim(mesh, _points.Count - 1, outerRadius, innerRadius);
        }

        mesh.Freeze();
        return mesh;
    }

    private void AddRim(MeshGeometry3D mesh, int pointIndex, double outerRadius, double innerRadius)
    {
        Point3D center = _points[pointIndex];
        Vector3D normal = _tangents[pointIndex];
        if (pointIndex == 0)
        {
            normal = -normal;
        }

        for (int side = 0; side < CircleSegments; side++)
        {
            int first = mesh.Positions.Count;
            Vector3D currentRadial = GetRadialDirection(pointIndex, side);
            Vector3D nextRadial = GetRadialDirection(pointIndex, side + 1);

            mesh.Positions.Add(center + currentRadial * outerRadius);
            mesh.Positions.Add(center + nextRadial * outerRadius);
            mesh.Positions.Add(center + nextRadial * innerRadius);
            mesh.Positions.Add(center + currentRadial * innerRadius);

            for (int vertex = 0; vertex < 4; vertex++)
            {
                mesh.Normals.Add(normal);
            }

            AddQuad(mesh, first, first + 1, first + 2, first + 3, pointIndex == 0);
        }
    }

    private Vector3D GetRadialDirection(int pointIndex, int side)
    {
        double angle = side * Math.Tau / CircleSegments;
        return _horizontalAxes[pointIndex] * Math.Cos(angle)
            + _verticalAxes[pointIndex] * Math.Sin(angle);
    }

    private void AddStraightSegment(Point3D start, Point3D end)
    {
        int segments = (int)Math.Clamp(Math.Ceiling((end - start).Length / StraightStep), 1, 512);
        for (int step = 0; step < segments; step++)
        {
            double progress = (double)step / segments;
            AddPoint(start + (end - start) * progress);
        }
    }

    private void AddPoint(Point3D point)
    {
        if (_points.Count == 0 || (point - _points[_points.Count - 1]).Length > PointTolerance)
        {
            _points.Add(point);
        }
    }

    private static Vector3D Normalize(Vector3D vector)
    {
        if (vector.Length < 0.0000000001)
        {
            return new Vector3D();
        }

        return vector / vector.Length;
    }

    private static void AddQuad(MeshGeometry3D mesh, int first, int second, int third, int fourth, bool reverse)
    {
        if (reverse)
        {
            AddTriangle(mesh, first, third, second);
            AddTriangle(mesh, first, fourth, third);
        }
        else
        {
            AddTriangle(mesh, first, second, third);
            AddTriangle(mesh, first, third, fourth);
        }
    }

    private static void AddTriangle(MeshGeometry3D mesh, int first, int second, int third)
    {
        mesh.TriangleIndices.Add(first);
        mesh.TriangleIndices.Add(second);
        mesh.TriangleIndices.Add(third);
    }
}
