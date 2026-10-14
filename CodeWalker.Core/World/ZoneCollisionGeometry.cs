using CodeWalker.GameFiles;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeWalker.World
{
    /// <summary>Convex collision shapes in source coordinates; all zone tests use package coordinates.</summary>
    public static class ZoneCollisionGeometry
    {
        public enum Kind { Box, Sphere, Capsule, Cylinder }
        public sealed class Shape
        {
            public Kind Type;
            public Vector3 A, B, X, Y, Z;
            public Vector3 Axis = Vector3.UnitZ;
            public float Radius;

            public Vector3 Support(Vector3 direction, Matrix transform)
            {
                var d = Vector3.TransformNormal(direction, Matrix.Transpose(transform));
                Vector3 p;
                if (Type == Kind.Box)
                    p = A + (Vector3.Dot(d, X) > 0 ? X : Vector3.Zero) + (Vector3.Dot(d, Y) > 0 ? Y : Vector3.Zero) + (Vector3.Dot(d, Z) > 0 ? Z : Vector3.Zero);
                else
                {
                    p = Type == Kind.Sphere || Vector3.Dot(d, B - A) <= 0 ? A : B;
                    if (Type == Kind.Cylinder)
                    {
                        var axis = (B - A).LengthSquared() > 1e-12f ? Vector3.Normalize(B - A) : Axis;
                        d -= axis * Vector3.Dot(d, axis);
                    }
                    if (d.LengthSquared() > 1e-20f) p += Vector3.Normalize(d) * Radius;
                }
                return Vector3.TransformCoordinate(p, transform);
            }

            public bool IsInside(ZoneClipper zone, Matrix transform)
            {
                if (Support(-Vector3.UnitZ, transform).Z < zone.MinZ - ZoneClipper.Epsilon
                    || Support(Vector3.UnitZ, transform).Z > zone.MaxZ + ZoneClipper.Epsilon) return false;
                Vector2[] hull;
                if (Type == Kind.Box)
                    hull = Hull(BoxCorners().Select(p => Vector3.TransformCoordinate(p, transform)).Select(p => new Vector2(p.X, p.Y)));
                else
                {
                    // Circumscribed support polygon: never preserve a curved shape merely because
                    // a few sampled surface vertices fit. Near a boundary this is conservative.
                    const int count = 128;
                    var normals = new Vector2[count]; var distances = new float[count];
                    for (int i = 0; i < count; i++)
                    {
                        double angle = i * Math.PI * 2 / count;
                        normals[i] = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                        var p = Support(new Vector3(normals[i], 0), transform);
                        distances[i] = Vector2.Dot(normals[i], new Vector2(p.X, p.Y));
                    }
                    hull = new Vector2[count];
                    for (int i = 0; i < count; i++)
                    {
                        int j = (i + 1) % count; var a = normals[i]; var b = normals[j]; float det = Cross(a, b);
                        hull[i] = new Vector2(distances[i] * b.Y - a.Y * distances[j], a.X * distances[j] - distances[i] * b.X) / det;
                    }
                }
                return ContainsHull(zone, hull);
            }

            public bool IsOutside(ZoneClipper zone, Matrix transform)
            {
                return Support(Vector3.UnitZ, transform).Z < zone.MinZ || Support(-Vector3.UnitZ, transform).Z > zone.MaxZ
                    || Support(Vector3.UnitX, transform).X < zone.Polygon.Min(p => p.X) || Support(-Vector3.UnitX, transform).X > zone.Polygon.Max(p => p.X)
                    || Support(Vector3.UnitY, transform).Y < zone.Polygon.Min(p => p.Y) || Support(-Vector3.UnitY, transform).Y > zone.Polygon.Max(p => p.Y);
            }

            private Vector3[] BoxCorners() => Enumerable.Range(0, 8).Select(i => A + ((i & 1) != 0 ? X : Vector3.Zero)
                + ((i & 2) != 0 ? Y : Vector3.Zero) + ((i & 4) != 0 ? Z : Vector3.Zero)).ToArray();

            public List<Vector3[]> Mesh(Matrix transform)
            {
                var result = new List<Vector3[]>();
                var center = Type == Kind.Box ? A + (X + Y + Z) * .5f : (A + B) * .5f;
                void Triangle(Vector3 a, Vector3 b, Vector3 c)
                {
                    if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-16f) return;
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3 - center) < 0) { var t = b; b = c; c = t; }
                    var tri = new[] { a, b, c }.Select(p => Vector3.TransformCoordinate(p, transform)).ToArray();
                    if (transform.Determinant() < 0) { var t = tri[1]; tri[1] = tri[2]; tri[2] = t; }
                    result.Add(tri);
                }
                if (Type == Kind.Box)
                {
                    var v = BoxCorners();
                    foreach (var f in new[] { new[] {0,1,3,2}, new[] {4,6,7,5}, new[] {0,4,5,1}, new[] {2,3,7,6}, new[] {0,2,6,4}, new[] {1,5,7,3} })
                    { Triangle(v[f[0]], v[f[1]], v[f[2]]); Triangle(v[f[0]], v[f[2]], v[f[3]]); }
                    return result;
                }
                var axis = B - A;
                if (axis.LengthSquared() < 1e-12f) axis = Axis; else axis.Normalize();
                var u = Vector3.Normalize(Vector3.Cross(axis, Math.Abs(axis.Z) < .9f ? Vector3.UnitZ : Vector3.UnitX));
                var vaxis = Vector3.Cross(axis, u);
                float scale = Math.Max(Vector3.TransformNormal(Vector3.UnitX, transform).Length(), Math.Max(Vector3.TransformNormal(Vector3.UnitY, transform).Length(), Vector3.TransformNormal(Vector3.UnitZ, transform).Length()));
                double radius = Math.Max(Radius * scale, .01f);
                int slices = Math.Max(16, Math.Min(128, (int)Math.Ceiling(Math.PI / Math.Acos(Math.Max(-1, 1 - .005 / radius)))));
                if ((slices & 1) != 0) slices++;
                var rings = new List<Vector3[]>();
                void Ring(Vector3 origin, float r) => rings.Add(Enumerable.Range(0, slices).Select(i => origin + r * (u * (float)Math.Cos(2 * Math.PI * i / slices) + vaxis * (float)Math.Sin(2 * Math.PI * i / slices))).ToArray());
                if (Type == Kind.Cylinder) { Ring(A, 0); Ring(A, Radius); Ring(B, Radius); Ring(B, 0); }
                else
                {
                    int half = slices / 4;
                    for (int i = 0; i <= half; i++) { double angle = -Math.PI / 2 + Math.PI / 2 * i / half; Ring(A + axis * (Radius * (float)Math.Sin(angle)), Radius * (float)Math.Cos(angle)); }
                    if (Type == Kind.Capsule && A != B) Ring(B, Radius);
                    for (int i = 1; i <= half; i++) { double angle = Math.PI / 2 * i / half; Ring(B + axis * (Radius * (float)Math.Sin(angle)), Radius * (float)Math.Cos(angle)); }
                }
                for (int j = 0; j + 1 < rings.Count; j++)
                    for (int i = 0; i < slices; i++)
                    { int n = (i + 1) % slices; Triangle(rings[j][i], rings[j][n], rings[j + 1][i]); Triangle(rings[j][n], rings[j + 1][n], rings[j + 1][i]); }
                return result;
            }
        }

        public static Shape FromBound(Bounds b)
        {
            if (b is BoundBox) return new Shape { Type = Kind.Box, A = b.BoxMin, X = new Vector3(b.BoxMax.X - b.BoxMin.X, 0, 0), Y = new Vector3(0, b.BoxMax.Y - b.BoxMin.Y, 0), Z = new Vector3(0, 0, b.BoxMax.Z - b.BoxMin.Z) };
            if (b is BoundSphere) return new Shape { Type = Kind.Sphere, A = b.SphereCenter, B = b.SphereCenter, Radius = b.SphereRadius };
            if (b is BoundCapsule) { var d = Vector3.UnitY * Math.Max(0, b.SphereRadius - b.Margin); return new Shape { Type = Kind.Capsule, A = b.SphereCenter - d, B = b.SphereCenter + d, Radius = b.Margin }; }
            if (b is BoundCylinder) { var d = Vector3.UnitY * ((b.BoxMax.Y - b.BoxMin.Y) / 2); return new Shape { Type = Kind.Cylinder, A = b.SphereCenter - d, B = b.SphereCenter + d, Radius = (b.BoxMax.X - b.BoxMin.X) / 2, Axis = Vector3.UnitY }; }
            if (b is BoundDisc) return new Shape { Type = Kind.Cylinder, A = b.SphereCenter - Vector3.UnitX * b.Margin, B = b.SphereCenter + Vector3.UnitX * b.Margin, Radius = b.SphereRadius, Axis = Vector3.UnitX };
            return null;
        }

        public static Shape FromPolygon(BoundGeometry owner, BoundPolygon polygon)
        {
            Vector3 P(int i) => owner.GetVertex(i) + owner.CenterGeom; // GetVertexPos already applies owner.Transform.
            if (polygon is BoundPolygonSphere s) return new Shape { Type = Kind.Sphere, A = P(s.sphereIndex), B = P(s.sphereIndex), Radius = s.sphereRadius };
            if (polygon is BoundPolygonCapsule c) return new Shape { Type = Kind.Capsule, A = P(c.capsuleIndex1), B = P(c.capsuleIndex2), Radius = c.capsuleRadius };
            if (polygon is BoundPolygonCylinder y) return new Shape { Type = Kind.Cylinder, A = P(y.cylinderIndex1), B = P(y.cylinderIndex2), Radius = y.cylinderRadius };
            if (polygon is BoundPolygonBox box)
            {
                var a = P(box.boxIndex1); var b = P(box.boxIndex2); var c1 = P(box.boxIndex3); var d = P(box.boxIndex4);
                var x = ((c1 + d) - (a + b)) * .5f;
                return new Shape { Type = Kind.Box, A = a, X = x, Y = c1 - (a + x), Z = d - (a + x) };
            }
            return null;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        private static Vector2[] Hull(IEnumerable<Vector2> input)
        {
            var points = input.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
            var hull = new List<Vector2>();
            foreach (var p in points) { while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            int lower = hull.Count;
            foreach (var p in points.Reverse().Skip(1)) { while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
            return hull.ToArray();
        }
        private static bool ContainsHull(ZoneClipper zone, Vector2[] hull)
        {
            if (hull.Length < 3 || hull.Any(p => !zone.Contains(new Vector3(p, (zone.MinZ + zone.MaxZ) / 2)))) return false;
            const float eps = ZoneClipper.Epsilon;
            // A concave notch may enter a shape even when every shape vertex is inside.
            foreach (var p in zone.Polygon)
                if (Enumerable.Range(0, hull.Length).All(i => Cross(hull[(i + 1) % hull.Length] - hull[i], p - hull[i]) > eps)) return false;
            for (int i = 0; i < hull.Length; i++)
                for (int j = 0; j < zone.Polygon.Length; j++)
                {
                    var a = hull[i]; var b = hull[(i + 1) % hull.Length]; var c = zone.Polygon[j]; var d = zone.Polygon[(j + 1) % zone.Polygon.Length];
                    if (Cross(b - a, c - a) * Cross(b - a, d - a) < -eps * eps && Cross(d - c, a - c) * Cross(d - c, b - c) < -eps * eps) return false;
                }
            return true;
        }

        /// <summary>Surface intersection plus caps on the actual zone boundary, never on internal triangulation edges.</summary>
        public static IEnumerable<Vector3[]> Cut(Shape shape, Matrix transform, ZoneClipper zone)
        {
            var mesh = shape.Mesh(transform);
            foreach (var triangle in mesh)
                foreach (var clipped in zone.Clip(new ZoneClipper.Vertex(triangle[0]), new ZoneClipper.Vertex(triangle[1]), new ZoneClipper.Vertex(triangle[2])))
                    yield return clipped.Select(v => v.Position).ToArray();
            var planes = new List<Tuple<Vector3, float, Vector3, float>> {
                Tuple.Create(-Vector3.UnitZ, -zone.MinZ, Vector3.Zero, 0f), Tuple.Create(Vector3.UnitZ, zone.MaxZ, Vector3.Zero, 0f) };
            for (int i = 0; i < zone.Polygon.Length; i++)
            {
                var a = zone.Polygon[i]; var delta = zone.Polygon[(i + 1) % zone.Polygon.Length] - a;
                var normal = new Vector3(delta.Y, -delta.X, 0); normal.Normalize();
                planes.Add(Tuple.Create(normal, Vector3.Dot(normal, new Vector3(a, 0)), new Vector3(a, 0), delta.Length()));
            }
            foreach (var plane in planes)
            {
                var n = plane.Item1; float distance = plane.Item2;
                var points = new List<Vector3>(); bool positive = false, negative = false;
                foreach (var t in mesh)
                    for (int i = 0; i < 3; i++)
                    {
                        var a = t[i]; var b = t[(i + 1) % 3]; float da = Vector3.Dot(n, a) - distance, db = Vector3.Dot(n, b) - distance;
                        positive |= da > ZoneClipper.Epsilon; negative |= da < -ZoneClipper.Epsilon;
                        if ((da >= 0) == (db >= 0)) continue;
                        var p = a + (b - a) * (da / (da - db));
                        if (!points.Any(q => (p - q).LengthSquared() < 1e-10f)) points.Add(p);
                    }
                if (!positive || !negative || points.Count < 3) continue;
                var center = points.Aggregate(Vector3.Zero, (a, b) => a + b) / points.Count;
                var u = Vector3.Normalize(points[0] - center); var vaxis = Vector3.Cross(n, u);
                points = points.OrderBy(p => Math.Atan2(Vector3.Dot(p - center, vaxis), Vector3.Dot(p - center, u))).ToList();
                if (plane.Item4 > 0)
                {
                    var along = new Vector3(-n.Y, n.X, 0);
                    points = CutRing(points, p => Vector3.Dot(p - plane.Item3, along));
                    points = CutRing(points, p => plane.Item4 - Vector3.Dot(p - plane.Item3, along));
                }
                for (int i = 1; i + 1 < points.Count; i++)
                    foreach (var clipped in zone.Clip(new ZoneClipper.Vertex(points[0]), new ZoneClipper.Vertex(points[i]), new ZoneClipper.Vertex(points[i + 1])))
                        yield return clipped.Select(v => v.Position).ToArray();
            }
        }
        private static List<Vector3> CutRing(List<Vector3> input, Func<Vector3, float> distance)
        {
            var output = new List<Vector3>(); if (input.Count == 0) return output;
            var a = input[input.Count - 1]; float da = distance(a);
            foreach (var b in input) { float db = distance(b); if ((da >= 0) != (db >= 0)) output.Add(a + (b - a) * (da / (da - db))); if (db >= 0) output.Add(b); a = b; da = db; }
            return output;
        }
    }
}
