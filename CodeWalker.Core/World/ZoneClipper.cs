using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeWalker.World
{
    /// <summary>A simple polygon extruded along Z. Positions use a local frame for precision.</summary>
    public sealed class ZoneClipper
    {
        public sealed class Vertex
        {
            public Vector3 Position;
            public float[] Attributes;
            public Vertex(Vector3 p, float[] attributes = null) { Position = p; Attributes = attributes ?? new float[0]; }
            internal static Vertex Lerp(Vertex a, Vertex b, float t)
            {
                var data = new float[a.Attributes.Length];
                for (int i = 0; i < data.Length; i++) data[i] = a.Attributes[i] + (b.Attributes[i] - a.Attributes[i]) * t;
                return new Vertex(Vector3.Lerp(a.Position, b.Position, t), data);
            }
        }

        public const float Epsilon = 0.00001f;
        public Vector2[] Polygon { get; }
        public float MinZ { get; }
        public float MaxZ { get; }
        private readonly List<Vector2[]> cells = new List<Vector2[]>();

        public ZoneClipper(IEnumerable<Vector2> polygon, float minZ, float maxZ)
        {
            Polygon = polygon.ToArray(); MinZ = minZ; MaxZ = maxZ;
            if (Polygon.Length < 3 || Polygon.Length > 256 || !Finite(minZ) || !Finite(maxZ) || maxZ <= minZ)
                throw new ArgumentException("Use 3–256 polygon points and a non-empty height range.");
            for (int i = 0; i < Polygon.Length; i++)
            {
                var a = Polygon[i]; var b = Polygon[(i + 1) % Polygon.Length];
                if (!Finite(a.X) || !Finite(a.Y) || (a - b).LengthSquared() < Epsilon * Epsilon)
                    throw new ArgumentException("The polygon contains an invalid or repeated point.");
                for (int j = i + 1; j < Polygon.Length; j++)
                {
                    if (j == i + 1 || (i == 0 && j == Polygon.Length - 1)) continue;
                    if (SegmentsIntersect(a, b, Polygon[j], Polygon[(j + 1) % Polygon.Length]))
                        throw new ArgumentException("The polygon intersects itself.");
                }
            }
            float area = 0;
            for (int i = 0; i < Polygon.Length; i++) area += Cross(Polygon[i], Polygon[(i + 1) % Polygon.Length]);
            if (Math.Abs(area) < Epsilon) throw new ArgumentException("The polygon has zero area.");
            if (area < 0) Array.Reverse(Polygon);
            var ring = Polygon.ToList();
            // Remove redundant collinear points before ear clipping.
            bool changed = true;
            while (changed && ring.Count > 3)
            {
                changed = false;
                for (int i = 0; i < ring.Count; i++)
                    if (Math.Abs(Side(ring[(i + ring.Count - 1) % ring.Count], ring[i], ring[(i + 1) % ring.Count])) < Epsilon)
                    { ring.RemoveAt(i); changed = true; break; }
            }
            while (ring.Count > 3)
            {
                bool found = false;
                for (int i = 0; i < ring.Count; i++)
                {
                    int previous = (i + ring.Count - 1) % ring.Count, next = (i + 1) % ring.Count;
                    var a = ring[previous]; var b = ring[i]; var c = ring[next];
                    if (Side(a, b, c) <= Epsilon) continue;
                    if (ring.Where((p, k) => k != previous && k != i && k != next).Any(p => InTriangle(p, a, b, c))) continue;
                    cells.Add(new[] { a, b, c }); ring.RemoveAt(i); found = true; break;
                }
                if (!found) throw new ArgumentException("Unable to triangulate this polygon; move nearly overlapping points.");
            }
            cells.Add(ring.ToArray());
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        private static float Side(Vector2 a, Vector2 b, Vector2 p) => Cross(b - a, p - a);
        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Side(a, b, p) >= -Epsilon && Side(b, c, p) >= -Epsilon && Side(c, a, p) >= -Epsilon;
        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            return Math.Max(a.X, b.X) + Epsilon >= Math.Min(c.X, d.X) && Math.Max(c.X, d.X) + Epsilon >= Math.Min(a.X, b.X)
                && Math.Max(a.Y, b.Y) + Epsilon >= Math.Min(c.Y, d.Y) && Math.Max(c.Y, d.Y) + Epsilon >= Math.Min(a.Y, b.Y)
                && Side(a, b, c) * Side(a, b, d) <= Epsilon * Epsilon && Side(c, d, a) * Side(c, d, b) <= Epsilon * Epsilon;
        }

        public bool Contains(Vector3 p) => p.Z >= MinZ - Epsilon && p.Z <= MaxZ + Epsilon
            && cells.Any(t => InTriangle(new Vector2(p.X, p.Y), t[0], t[1], t[2]));

        public Vector2[][] GetTriangles() => cells.Select(t => t.ToArray()).ToArray();

        /// <summary>Returns triangles, interpolating all continuous vertex attributes at cuts. No caps.</summary>
        public List<Vertex[]> Clip(Vertex a, Vertex b, Vertex c)
        {
            var result = new List<Vertex[]>();
            foreach (var cell in cells)
            {
                var poly = new List<Vertex> { a, b, c };
                for (int edge = 0; edge < 3 && poly.Count >= 3; edge++)
                {
                    var p = cell[edge]; var q = cell[(edge + 1) % 3];
                    var length = (q - p).Length();
                    poly = Cut(poly, v => Side(p, q, new Vector2(v.X, v.Y)) / length);
                }
                poly = Cut(poly, v => v.Z - MinZ);
                poly = Cut(poly, v => MaxZ - v.Z);
                for (int i = 1; i + 1 < poly.Count; i++)
                {
                    var tri = new[] { poly[0], poly[i], poly[i + 1] };
                    if (Vector3.Cross(tri[1].Position - tri[0].Position, tri[2].Position - tri[0].Position).LengthSquared() < 1e-14f) continue;
                    // A vertical source triangle can lie exactly on an internal triangulation edge.
                    if (result.Any(t => tri.All(v => t.Any(w => (v.Position - w.Position).LengthSquared() < 1e-12f)))) continue;
                    result.Add(tri);
                }
            }
            return result;
        }

        private static List<Vertex> Cut(List<Vertex> input, Func<Vector3, float> distance)
        {
            var output = new List<Vertex>();
            if (input.Count < 3) return output;
            var a = input[input.Count - 1]; float da = distance(a.Position);
            foreach (var b in input)
            {
                float db = distance(b.Position);
                bool ina = da >= 0, inb = db >= 0;
                if (ina != inb) output.Add(Vertex.Lerp(a, b, da / (da - db)));
                if (inb) output.Add(b);
                a = b; da = db;
            }
            return output;
        }
    }
}
