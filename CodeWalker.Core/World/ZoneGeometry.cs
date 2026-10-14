using CodeWalker.GameFiles;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CodeWalker.World
{
    /// <summary>Geometry shared by the zone editor and its headless tests.</summary>
    public static class ZoneGeometry
    {
        public static Ray ClickRay(Matrix inverseViewProjection, Vector3 cameraPosition, bool orthographic, float x, float y, int width, int height)
        {
            var p = new Vector3(2 * x / Math.Max(1, width) - 1, 1 - 2 * y / Math.Max(1, height), 1);
            var near = Vector3.TransformCoordinate(p, inverseViewProjection);
            p.Z = 0;
            var far = Vector3.TransformCoordinate(p, inverseViewProjection);
            return new Ray(cameraPosition + (orthographic ? near : Vector3.Zero), Vector3.Normalize(far - near));
        }

        public static DrawableModel[] BestModels(DrawableBase drawable) => drawable.DrawableModels?.High
            ?? drawable.DrawableModels?.Med ?? drawable.DrawableModels?.Low ?? drawable.DrawableModels?.VLow ?? new DrawableModel[0];

        // The inverse-transformed direction is deliberately NOT normalized: t stays in world metres under nonuniform scale.
        public static float Raycast(DrawableBase drawable, Matrix transform, Ray worldRay, float limit)
        {
            foreach (var model in BestModels(drawable))
                if (model.HasSkin == 0)
                    foreach (var geometry in model.Geometries ?? new DrawableGeometry[0]) limit = Raycast(geometry, transform, worldRay, limit);
            return limit;
        }

        public static float Raycast(DrawableGeometry geometry, Matrix transform, Ray worldRay, float limit)
        {
            if (Math.Abs(transform.Determinant()) < 1e-12f) return limit;
            var inverse = Matrix.Invert(transform);
            var ray = new Ray { Position = Vector3.TransformCoordinate(worldRay.Position, inverse), Direction = Vector3.TransformNormal(worldRay.Direction, inverse) };
            var box = new BoundingBox(geometry.AABB.Min.XYZ(), geometry.AABB.Max.XYZ());
            if (!ray.Intersects(ref box)) return limit;
            var vertices = geometry.VertexData; var indices = geometry.IndexBuffer?.Indices;
            if (vertices == null || indices == null) return limit;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                var a = vertices.GetVector3(indices[i], 0); var b = vertices.GetVector3(indices[i + 1], 0); var c = vertices.GetVector3(indices[i + 2], 0);
                // Two-sided Moller-Trumbore; transparent texels are still part of the geometric surface.
                var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(ray.Direction, e2);
                float det = Vector3.Dot(e1, p);
                if (Math.Abs(det) < 1e-10f) continue;
                var s = ray.Position - a; float u = Vector3.Dot(s, p) / det;
                if (u < 0 || u > 1) continue;
                var q = Vector3.Cross(s, e1); float v = Vector3.Dot(ray.Direction, q) / det;
                if (v < 0 || u + v > 1) continue;
                float t = Vector3.Dot(e2, q) / det;
                if (t > 0 && t < limit) limit = t;
            }
            return limit;
        }

        public static Vector3[] Triangles(DrawableBase drawable, Matrix transform, CancellationToken token)
        {
            if (drawable.Skeleton != null) throw new NotSupportedException("Pre-draw currently requires a static mesh without a skeleton.");
            var result = new List<Vector3>();
            foreach (var model in BestModels(drawable))
                foreach (var g in model.Geometries ?? new DrawableGeometry[0])
                {
                    token.ThrowIfCancellationRequested();
                    if (model.HasSkin != 0) throw new NotSupportedException("Skinned contour not supported.");
                    if (g.VertexData == null || g.IndexBuffer?.Indices == null) continue;
                    foreach (var index in g.IndexBuffer.Indices) result.Add(Vector3.TransformCoordinate(g.VertexData.GetVector3(index, 0), transform));
                }
            return result.ToArray();
        }

        public sealed class Footprint
        {
            public Vector2[] Points;
            public float CellSize, Simplification;
            public bool BridgedIslands;
            public int IgnoredHoles;
        }

        /// <summary>Conservative triangle coverage on an adaptive XY grid, followed by boundary tracing.
        /// A single simple polygon is required by the exporter; disconnected islands are enclosed by a hull.</summary>
        public static Footprint TraceFootprint(Vector3[] triangles, float precision, CancellationToken token)
        {
            if (triangles.Length < 3 || precision <= 0) throw new ArgumentException("No mesh triangles selected.");
            var min = triangles.Aggregate(Vector3.Min); var max = triangles.Aggregate(Vector3.Max);
            float cell = Math.Max(precision, Math.Max(max.X - min.X, max.Y - min.Y) / 2044f);
            var origin = new Vector2(min.X - cell, min.Y - cell);
            int w = (int)Math.Ceiling((max.X - min.X) / cell) + 4, h = (int)Math.Ceiling((max.Y - min.Y) / cell) + 4;
            var spans = new int[(w + 1) * h];
            // Each projected triangle intersects a row in one interval. Difference spans avoid visiting every covered cell per face.
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                var t = new[] { new Vector2(triangles[i].X, triangles[i].Y), new Vector2(triangles[i + 1].X, triangles[i + 1].Y), new Vector2(triangles[i + 2].X, triangles[i + 2].Y) };
                int y0 = Math.Max(0, (int)Math.Floor((t.Min(p => p.Y) - origin.Y) / cell));
                int y1 = Math.Min(h - 1, (int)Math.Floor((t.Max(p => p.Y) - origin.Y) / cell));
                for (int y = y0; y <= y1; y++)
                {
                    float lo = origin.Y + y * cell, hi = lo + cell, left = float.MaxValue, right = float.MinValue;
                    void Include(float x) { left = Math.Min(left, x); right = Math.Max(right, x); }
                    for (int e = 0; e < 3; e++)
                    {
                        var a = t[e]; var b = t[(e + 1) % 3];
                        if (a.Y >= lo && a.Y <= hi) Include(a.X);
                        if (a.Y == b.Y) continue;
                        foreach (float boundary in new[] { lo, hi })
                        {
                            float k = (boundary - a.Y) / (b.Y - a.Y);
                            if (k >= 0 && k <= 1) Include(a.X + k * (b.X - a.X));
                        }
                    }
                    if (left > right) continue;
                    int x0 = Math.Max(0, (int)Math.Floor((left - origin.X) / cell)), x1 = Math.Min(w - 1, (int)Math.Floor((right - origin.X) / cell));
                    spans[y * (w + 1) + x0]++; spans[y * (w + 1) + x1 + 1]--;
                }
            }
            var filled = new bool[w * h];
            for (int y = 0; y < h; y++)
            { int sum = 0; for (int x = 0; x < w; x++) { sum += spans[y * (w + 1) + x]; filled[y * w + x] = sum > 0; } }
            bool Has(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && filled[y * w + x];
            int Key(int x, int y) => y * (w + 1) + x;
            Vector2 Grid(int k) => new Vector2(k % (w + 1), k / (w + 1));
            var edges = new Dictionary<int, List<int>>();
            void Edge(int a, int b) { if (!edges.TryGetValue(a, out var list)) edges[a] = list = new List<int>(); list.Add(b); }
            for (int y = 0; y < h; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < w; x++) if (Has(x, y))
                {
                    if (!Has(x, y - 1)) Edge(Key(x, y), Key(x + 1, y));
                    if (!Has(x + 1, y)) Edge(Key(x + 1, y), Key(x + 1, y + 1));
                    if (!Has(x, y + 1)) Edge(Key(x + 1, y + 1), Key(x, y + 1));
                    if (!Has(x - 1, y)) Edge(Key(x, y + 1), Key(x, y));
                }
            }
            var loops = new List<Vector2[]>(); int holes = 0;
            while (edges.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                int start = edges.First().Key, current = start; Vector2 direction = Vector2.UnitX;
                var ring = new List<Vector2>();
                do
                {
                    ring.Add(Grid(current));
                    var choices = edges[current];
                    int next = choices.OrderByDescending(k => { var d = Grid(k) - Grid(current); return 2 * Cross(direction, d) + Vector2.Dot(direction, d); }).First();
                    direction = Grid(next) - Grid(current); choices.Remove(next); if (choices.Count == 0) edges.Remove(current); current = next;
                } while (current != start);
                float area = 0; for (int i = 0; i < ring.Count; i++) area += Cross(ring[i], ring[(i + 1) % ring.Count]);
                if (area > 0) loops.Add(ring.Select(p => origin + p * cell).ToArray()); else holes++;
            }
            if (loops.Count == 0) throw new ArgumentException("No projected mesh footprint found.");
            bool bridged = loops.Count > 1;
            var outline = bridged ? Hull(loops.SelectMany(p => p)) : loops[0];
            float tolerance = cell * 0.5f;
            Vector2[] simplified;
            while (true)
            {
                simplified = Simplify(outline, tolerance);
                try { new ZoneClipper(simplified.Select(p => p - simplified[0]), 0, 1); break; }
                catch (ArgumentException)
                {
                    if (simplified.Length <= 256) { simplified = Hull(outline); bridged = true; break; }
                    tolerance *= 1.5f;
                }
            }
            return new Footprint { Points = simplified, CellSize = cell, Simplification = tolerance, BridgedIslands = bridged, IgnoredHoles = holes };
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        private static Vector2[] Hull(IEnumerable<Vector2> vertices)
        {
            var sorted = vertices.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray(); var hull = new List<Vector2>();
            void Push(Vector2 p, int floor) { while (hull.Count >= floor + 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            foreach (var p in sorted) Push(p, 0);
            int count = hull.Count - 1;
            for (int i = sorted.Length - 2; i >= 0; i--) Push(sorted[i], count);
            hull.RemoveAt(hull.Count - 1); return hull.ToArray();
        }
        private static Vector2[] Simplify(Vector2[] ring, float tolerance)
        {
            int split = Enumerable.Range(1, ring.Length - 1).OrderByDescending(i => (ring[i] - ring[0]).LengthSquared()).First();
            var result = new List<Vector2>();
            void Segment(int a, int b)
            {
                var p = ring[a % ring.Length]; var d = ring[b % ring.Length] - p; float len = d.LengthSquared(), maximum = tolerance * tolerance; int far = -1;
                for (int i = a + 1; i < b; i++)
                {
                    var q = ring[i % ring.Length] - p; float t = len > 0 ? Math.Max(0, Math.Min(1, Vector2.Dot(q, d) / len)) : 0;
                    float dist = (q - t * d).LengthSquared(); if (dist > maximum) { maximum = dist; far = i; }
                }
                if (far >= 0) { Segment(a, far); Segment(far, b); } else result.Add(p);
            }
            Segment(0, split); Segment(split, ring.Length); return result.ToArray();
        }
    }
}
