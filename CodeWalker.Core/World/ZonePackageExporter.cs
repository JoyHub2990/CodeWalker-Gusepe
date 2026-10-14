using CodeWalker.GameFiles;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

namespace CodeWalker.World
{
    /// <summary>Experimental static-world exporter. All edits are made on independent resource copies.</summary>
    public sealed class ZonePackageExporter
    {
        public sealed class Asset
        {
            public YmapEntityDef Entity;
            public Drawable Drawable;
            public FragDrawable FragmentDrawable;
            public List<Texture> Textures = new List<Texture>();
        }
        public sealed class Request
        {
            public string Name, OutputDirectory, ImporterPath;
            public Vector3 Pivot;
            public Vector2[] Polygon;
            public float MinZ, MaxZ;
            public List<Asset> Assets = new List<Asset>();
            public List<YbnFile> Collisions = new List<YbnFile>();
            public List<YmapFile> Maps = new List<YmapFile>();
            public List<string> Warnings = new List<string>();
        }
        public sealed class Result
        {
            public string Directory, FinalizationNotice;
            public int Assets, References, CollisionTriangles, OcclusionTriangles;
            public int CollisionPrimitives, CutCollisionPrimitives, PropCollisionParts;
            public List<string> Warnings = new List<string>();
        }

        private readonly Request request;
        private readonly Result result = new Result();
        private readonly ZoneClipper clipper;
        private readonly CancellationToken token;
        private readonly List<string> manifestAssets = new List<string>();
        private readonly List<string> manifestReferences = new List<string>();
        private readonly Dictionary<string, List<Bounds>> collisionLayers = new Dictionary<string, List<Bounds>> {
            { "", new List<Bounds>() }, { "hi@", new List<Bounds>() }, { "ma@", new List<Bounds>() } };
        private string collisionPrefix = "";
        private List<Bounds> collisionChildren => collisionLayers[collisionPrefix];
        private readonly List<string> manifestCollisions = new List<string>();
        private readonly List<YmapOccludeModel> occluders = new List<YmapOccludeModel>();
        private readonly List<string> collisionReport = new List<string>();
        private readonly List<string> lodReport = new List<string>();
        private string stage;
        private string operation = "Starting zone export";

        public ZonePackageExporter(Request request, CancellationToken token)
        {
            this.request = request; this.token = token;
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 48 || request.Name.Any(c => !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_'))
                throw new ArgumentException("Package name: 1–48 lowercase letters, digits or underscores.");
            clipper = new ZoneClipper(request.Polygon.Select(p => p - new Vector2(request.Pivot.X, request.Pivot.Y)), request.MinZ - request.Pivot.Z, request.MaxZ - request.Pivot.Z);
            result.Warnings.AddRange(request.Warnings);
        }

        public Result Export(Action<string> progress = null)
        {
            string destination = Path.Combine(request.OutputDirectory, request.Name);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new package name: the output path already exists.");
            stage = Path.Combine(request.OutputDirectory, request.Name + ".partial-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(stage);
            result.Directory = stage;
            try
            {
                var map = NewMap(request.Name);
                var occlusionMap = NewMap(request.Name + "_occl");
                var types = new YtypFile { Name = request.Name + ".ytyp", NameHash = JenkHash.GenHash(request.Name) };
                types._CMapTypes.name = types.NameHash;
                var lods = new Dictionary<int, List<Drawable>>();
                foreach (var asset in request.Assets)
                {
                    token.ThrowIfCancellationRequested();
                    var entity = asset.Entity;
                    operation = (IsHd(entity) ? "Referencing " : "Clipping ") + entity.Name;
                    progress?.Invoke(operation);
                    if (IsHd(entity))
                    {
                        if (entity.IsMlo) { Warn(entity.Name + ": MLO instances are not supported; omitted."); continue; }
                        WriteReference(entity, map);
                        // Read bounds only: HD meshes, materials and lights remain in their original resources.
                        ClipPropBounds(asset);
                        continue;
                    }
                    if (entity.IsMlo || asset.Drawable == null || asset.Drawable.Skeleton != null)
                    { Warn(entity.Name + ": MLO, fragment or skeletal drawable not supported in v1; omitted."); continue; }
                    if (entity.Scale.X <= 0 || entity.Scale.Y <= 0 || entity.Scale.Z <= 0)
                    { Warn(entity.Name + ": non-positive scale not supported; omitted."); continue; }
                    if ((entity.Extensions?.Length ?? 0) > 0 || (entity.Archetype?.Extensions?.Length ?? 0) > 0)
                        Warn(entity.Name + ": entity/archetype extensions are not exported in v1.");
                    if (entity.Archetype is TimeArchetype) Warn(entity.Name + ": time archetype exported as a static variant.");
                    var draw = CloneDrawable(asset);
                    var transform = Matrix.Scaling(entity.Scale) * Matrix.RotationQuaternion(entity.Orientation) * Matrix.Translation(entity.Position - request.Pivot);
                    // The package uses centralized collisions; remove them from the generated YDRs.
                    ClipPropBounds(asset);
                    draw.Bound = null;
                    ClipDrawable(draw, transform);
                    int level = (int)entity._CEntityDef.lodLevel;
                    var models = draw.DrawableModels.High ?? draw.DrawableModels.Med ?? draw.DrawableModels.Low ?? draw.DrawableModels.VLow ?? new DrawableModel[0];
                    int triangles = models.Sum(m => (m.Geometries ?? new DrawableGeometry[0]).Sum(g => (g.IndexBuffer?.Indices?.Length ?? 0) / 3));
                    lodReport.Add(entity._CEntityDef.lodLevel + " | " + entity.Ymap?.Name + " #" + entity.Index + " | " + entity.Name
                        + " | dictionary=" + entity.Archetype?.DrawableDict + " | " + triangles + " triangles after clipping");
                    if (draw.AllModels.Length == 0 && (draw.LightAttributes?.data_items?.Length ?? 0) == 0) continue;
                    if (!lods.TryGetValue(level, out var list)) lods[level] = list = new List<Drawable>();
                    list.Add(draw);
                }
                foreach (var pair in lods.OrderBy(p => p.Key))
                {
                    token.ThrowIfCancellationRequested();
                    string suffix = pair.Key == 1 ? "lod" : "slod" + (pair.Key == 6 ? 4 : pair.Key - 1);
                    operation = "Merging " + request.Name + "_" + suffix;
                    var definition = new CEntityDef { lodLevel = (rage__eLodType)pair.Key, lodDist = pair.Key == 1 ? 700 : 2500, childLodDist = 350, scaleXY = 1, scaleZ = 1 };
                    WriteAsset(request.Name + "_" + suffix, Merge(pair.Value), map, types, definition, suffix, "merged and clipped");
                }
                BuildLodLinks(map);
                foreach (var ybn in request.Collisions)
                {
                    token.ThrowIfCancellationRequested(); operation = "Clipping collision " + ybn.Name; progress?.Invoke(operation);
                    collisionPrefix = CollisionPrefix(ybn.Name ?? ybn.RpfFileEntry?.Name);
                    int trianglesBefore = result.CollisionTriangles, nativeBefore = result.CollisionPrimitives;
                    ClipBounds(ybn.Bounds, Matrix.Translation(-request.Pivot), ybn.Name);
                    collisionReport.Add(ybn.Name + ": " + (result.CollisionTriangles - trianglesBefore) + " triangles, " + (result.CollisionPrimitives - nativeBefore) + " native primitives -> " + collisionPrefix + request.Name + ".ybn.");
                }
                collisionPrefix = "";
                foreach (var source in request.Maps)
                {
                    token.ThrowIfCancellationRequested();
                    operation = "Clipping occlusion from " + source.Name;
                    if ((source.LODLights?.LodLights?.Length ?? 0) > 0 || source.DistantLODLights != null)
                        Warn(source.Name + ": map LOD lights are not exported in v1 (drawable lights are retained).");
                    foreach (var box in source.BoxOccluders ?? new YmapBoxOccluder[0])
                        ClipOcclusion(box.GetTriangleVertices().Select(v => v.Position), 0, occlusionMap);
                    foreach (var model in source.OccludeModels ?? new YmapOccludeModel[0])
                        ClipOcclusion((model.GetTriangleVertices() ?? new EditorVertex[0]).Select(v => v.Position), model._OccludeModel.flags, occlusionMap);
                }
                occlusionMap.OccludeModels = occluders.ToArray();
                WriteMap(map, request.Name);
                WriteMap(occlusionMap, request.Name + "_occl");
                operation = "Writing " + request.Name + ".ytyp";
                var typeBytes = types.Save(); var verifyTypes = new YtypFile(); verifyTypes.Load(typeBytes);
                if ((verifyTypes.AllArchetypes?.Length ?? 0) != (types.AllArchetypes?.Length ?? 0)) throw new InvalidDataException("YTYP verification failed.");
                File.WriteAllBytes(Path.Combine(stage, request.Name + ".ytyp"), typeBytes);
                File.WriteAllText(Path.Combine(stage, request.Name + ".ytyp.xml"), MetaXml.GetXml(verifyTypes, out string typeXmlName));
                foreach (var layer in collisionLayers.Where(l => l.Value.Count > 0))
                {
                    string filename = layer.Key + request.Name + ".ybn";
                    operation = "Writing " + filename;
                    var root = new BoundComposite { Type = BoundsType.Composite, Children = new ResourcePointerArray64<Bounds> { data_items = layer.Value.ToArray() } };
                    foreach (var child in layer.Value) child.Parent = root;
                    var worldBoxes = layer.Value.Select(c => new BoundingBox(c.BoxMin, c.BoxMax).Transform(c.Transform)).ToArray();
                    root.BoxMin = worldBoxes.Select(b => b.Minimum).Aggregate(Vector3.Min);
                    root.BoxMax = worldBoxes.Select(b => b.Maximum).Aggregate(Vector3.Max);
                    root.BoxCenter = root.SphereCenter = (root.BoxMin + root.BoxMax) * 0.5f;
                    root.SphereRadius = (root.BoxMax - root.BoxCenter).Length();
                    root.UpdateChildrenFlags(); root.UpdateChildrenTransformations(); root.UpdateChildrenBounds(); root.BuildBVH();
                    var ybn = new YbnFile { Bounds = root };
                    var bytes = ybn.Save(); var check = new YbnFile(); check.Load(bytes);
                    if (!(check.Bounds is BoundComposite)) throw new InvalidDataException("YBN verification failed.");
                    int Count(Bounds b, bool triangles) => b is BoundComposite c ? (c.Children?.data_items ?? new Bounds[0]).Sum(child => Count(child, triangles))
                        : b is BoundGeometry g ? (g.Polygons ?? new BoundPolygon[0]).Count(p => (p is BoundPolygonTriangle) == triangles) : (triangles ? 0 : 1);
                    int triangleCount = Count(root, true), primitives = Count(root, false);
                    if (Count(check.Bounds, true) != triangleCount || Count(check.Bounds, false) != primitives)
                        throw new InvalidDataException("YBN verification failed: collision primitives or triangles were lost during serialization.");
                    VerifyCollisionCopy(root, check.Bounds);
                    File.WriteAllBytes(Path.Combine(stage, filename), bytes);
                    File.WriteAllText(Path.Combine(stage, filename + ".xml"), YbnXml.GetXml(ybn));
                    string role = layer.Key == "hi@" ? "high" : layer.Key == "ma@" ? "material" : "standard";
                    manifestCollisions.Add("{\"file\":" + Q(filename) + ",\"xml\":" + Q(filename + ".xml") + ",\"role\":" + Q(role)
                        + ",\"triangles\":" + triangleCount + ",\"native_primitives\":" + primitives + "}");
                    collisionReport.Add("Output " + filename + ": " + triangleCount + " triangles, " + primitives + " native primitives; local bounds " + V(root.BoxMin) + " .. " + V(root.BoxMax));
                }
                if (manifestCollisions.Count == 0) Warn("No collision geometry remains in the zone; no YBN was generated.");
                if (result.Assets == 0 && result.References == 0 && occluders.Count == 0 && manifestCollisions.Count == 0)
                    throw new InvalidOperationException("The zone contains no exportable geometry. See the report in " + stage);
                operation = "Finalizing " + request.Name;
                if (!string.IsNullOrEmpty(request.ImporterPath))
                    File.Copy(request.ImporterPath, Path.Combine(stage, "import_zone_package.py"));
                WriteManifest();
                File.WriteAllText(Path.Combine(stage, "REPORT.txt"), Report());
                File.WriteAllText(Path.Combine(stage, "COLLISIONS.txt"), "Collision contributions after clipping (per source/placement):\r\n" + string.Join("\r\n", collisionReport));
                File.WriteAllText(Path.Combine(stage, "LODS.txt"), "LOD contributions after clipping (levels from YMAP, not filename suffixes):\r\n" + string.Join("\r\n", lodReport));
                if (ZonePackageFinalizer.TryRename(stage, destination, token, progress)) result.Directory = destination;
                else
                {
                    result.FinalizationNotice = "Export finished, but Windows blocked the folder rename. Use this folder as the completed package despite its .partial suffix: " + stage
                        + "\nThe resource warnings in REPORT.txt still apply. No new export is required.";
                    File.WriteAllText(Path.Combine(stage, "FINALIZATION.txt"), result.FinalizationNotice);
                    progress?.Invoke(result.FinalizationNotice);
                }
                return result;
            }
            catch (Exception ex)
            {
                string report = Path.Combine(stage, "FAILED.txt");
                File.WriteAllText(report, "Operation: " + operation + Environment.NewLine + ex + Environment.NewLine + Report());
                if (ex is OperationCanceledException) throw;
                throw new InvalidOperationException(operation + "\n" + ex.Message + "\nDetails: " + report, ex);
            }
        }

        private void Warn(string message) { if (!result.Warnings.Contains(message)) result.Warnings.Add(message); }
        public static void VerifyCollisionCopy(Bounds expected, Bounds actual)
            => VerifyCollisionCopy(expected, actual, "root");

        private static void VerifyCollisionCopy(Bounds expected, Bounds actual, string path)
        {
            if (expected == null && actual == null) return;
            if (expected == null || actual == null || expected.Type != actual.Type) throw new InvalidDataException("YBN verification failed: bound type changed.");
            var axes = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            foreach (var axis in axes)
                if ((Vector3.TransformCoordinate(axis, expected.Transform) - Vector3.TransformCoordinate(axis, actual.Transform)).Length() > .0001f)
                    throw new InvalidDataException("YBN verification failed: collision placement changed.");
            if (expected is BoundComposite composite)
            {
                var a = composite.Children.data_items; var b = ((BoundComposite)actual).Children.data_items;
                if (a.Length != b.Length) throw new InvalidDataException("YBN verification failed: bound count changed.");
                for (int i = 0; i < a.Length; i++) VerifyCollisionCopy(a[i], b[i], path + "/" + i);
            }
            else if (expected is BoundGeometry geometry)
            {
                var other = (BoundGeometry)actual;
                if (geometry.Vertices.Length != other.Vertices.Length) throw new InvalidDataException("YBN verification failed: vertex count changed.");
                float tolerance = Math.Max(.0001f, other.Quantum.Length() * 2);
                for (int i = 0; i < geometry.Vertices.Length; i++)
                {
                    float error = (geometry.Vertices[i] + geometry.CenterGeom - other.Vertices[i] - other.CenterGeom).Length();
                    if (float.IsNaN(error) || float.IsInfinity(error) || error > tolerance)
                        throw new InvalidDataException("YBN verification failed: collision vertices moved during quantization. Bound " + path
                            + ", vertex " + i + ", error=" + F(error) + ", tolerance=" + F(tolerance)
                            + ", expected=" + V(geometry.Vertices[i]) + ", actual=" + V(other.Vertices[i])
                            + ", center=" + V(geometry.CenterGeom) + ", reloaded center=" + V(other.CenterGeom) + ", quantum=" + V(other.Quantum));
                }
            }
        }
        public static string CollisionPrefix(string name)
        {
            name = Path.GetFileName(name ?? "");
            return name.StartsWith("hi@", StringComparison.OrdinalIgnoreCase) ? "hi@" : name.StartsWith("ma@", StringComparison.OrdinalIgnoreCase) ? "ma@" : "";
        }
        public static bool IsHd(YmapEntityDef entity) => (int)entity._CEntityDef.lodLevel == 0 || (int)entity._CEntityDef.lodLevel == 5;
        private string Report() => "Experimental zone package v1.1.7\r\n" + result.Assets + " generated drawables, " + result.References + " original HD references, " + result.CollisionTriangles + " collision triangles, " + result.OcclusionTriangles + " occlusion triangles.\r\n"
            + result.CollisionPrimitives + " native collision primitives retained, " + result.CutCollisionPrimitives + " primitives converted/clipped, " + result.PropCollisionParts + " prop collision parts processed.\r\nHD resources remain external and uncut; original drawable collisions may coexist with the generated YBN.\r\n" + string.Join("\r\n", result.Warnings);
        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        private static string V(Vector3 v) => "[" + F(v.X) + "," + F(v.Y) + "," + F(v.Z) + "]";
        private void WriteManifest()
        {
            string json = "{\n\"schema\":\"ragequit.zone-package\",\"version\":3,\"name\":" + Q(request.Name)
                + ",\"status\":" + Q(result.Warnings.Count == 0 ? "complete" : "incomplete")
                + ",\"pivot_world\":" + V(request.Pivot) + ",\"min_z_world\":" + F(request.MinZ) + ",\"max_z_world\":" + F(request.MaxZ)
                + ",\"polygon_world\":[" + string.Join(",", request.Polygon.Select(p => "[" + F(p.X) + "," + F(p.Y) + "]")) + "]"
                + ",\"assets\":[" + string.Join(",\n", manifestAssets) + "]"
                + ",\"references\":[" + string.Join(",\n", manifestReferences) + "]"
                + ",\"collision\":" + (collisionChildren.Count > 0 ? Q(request.Name + ".ybn") : "null")
                + ",\"collisions\":[" + string.Join(",", manifestCollisions) + "]"
                + ",\"collision_stats\":{\"triangles\":" + result.CollisionTriangles + ",\"native_primitives\":" + result.CollisionPrimitives + ",\"clipped_primitives\":" + result.CutCollisionPrimitives + ",\"prop_parts\":" + result.PropCollisionParts + "}"
                + ",\"entities\":" + Q(request.Name + ".ymap") + ",\"occlusion\":" + Q(request.Name + "_occl.ymap")
                + ",\"warnings\":[" + string.Join(",", result.Warnings.Select(Q)) + "]\n}";
            File.WriteAllText(Path.Combine(stage, "manifest.json"), json, new UTF8Encoding(false));
        }

        private void WriteReference(YmapEntityDef source, YmapFile map)
        {
            var def = source._CEntityDef;
            def.position = source.Position - request.Pivot;
            def.parentIndex = -1; def.numChildren = 0; def.extensions = new Array_StructurePointer();
            var entity = new YmapEntityDef(map, 0, ref def) { Extensions = source.Extensions?.ToArray() };
            entity.SetArchetype(source.Archetype);
            map.AddEntity(entity);
            var rotation = source.Orientation;
            manifestReferences.Add("{\"archetype\":" + Q(source.Name) + ",\"archetype_hash\":" + source._CEntityDef.archetypeName.Hash
                + ",\"asset_name\":" + Q(source.Archetype?.AssetName ?? source.Name)
                + ",\"asset_type\":" + Q(source.Archetype?.BaseArchetypeDef.assetType == rage__fwArchetypeDef__eAssetType.ASSET_TYPE_FRAGMENT ? "fragment" : "drawable")
                + ",\"position\":" + V(entity.Position) + ",\"rotation_xyzw\":[" + F(rotation.X) + "," + F(rotation.Y) + "," + F(rotation.Z) + "," + F(rotation.W) + "]"
                + ",\"scale\":" + V(source.Scale) + ",\"role\":\"base\"}");
            result.References++;
        }

        private Drawable CloneDrawable(Asset asset)
        {
            // XML roundtrip deep-copies geometry, lights, bounds and shader data, including DDS payloads.
            var temporary = (Drawable)asset.Drawable.ShallowCopy();
            var textures = asset.Textures.Concat(asset.Drawable.ShaderGroup?.TextureDictionary?.Textures?.data_items ?? new Texture[0])
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
            var dict = new TextureDictionary(); dict.BuildFromTextureList(textures);
            temporary.ShaderGroup = new ShaderGroup { Shaders = asset.Drawable.ShaderGroup?.Shaders, TextureDictionary = dict };
            string folder = Path.Combine(stage, "textures"); Directory.CreateDirectory(folder);
            return XmlYdr.GetYdr(YdrXml.GetXml(new YdrFile { Drawable = temporary }, folder), folder).Drawable;
        }

        private void ClipDrawable(Drawable draw, Matrix transform)
        {
            var normalMatrix = Matrix.Transpose(Matrix.Invert(transform));
            DrawableModel[] ClipModels(DrawableModel[] models)
            {
                var output = new List<DrawableModel>();
                foreach (var model in models ?? new DrawableModel[0])
                {
                    if (model.HasSkin != 0) throw new NotSupportedException("Skinned geometry cannot be clipped by the static exporter.");
                    var geometries = new List<DrawableGeometry>();
                    foreach (var geom in model.Geometries ?? new DrawableGeometry[0])
                    {
                        token.ThrowIfCancellationRequested();
                        var data = geom.VertexData;
                        if (data == null || geom.IndexBuffer?.Indices == null) continue;
                        var offsets = new Dictionary<int, int>(); int width = 0;
                        for (int c = 0; c < 16; c++) if ((data.Info.Flags & (1u << c)) != 0)
                        { offsets[c] = width; width += VertexComponentTypes.GetComponentCount(data.Info.GetComponentType(c)); }
                        if (!offsets.ContainsKey(0)) throw new InvalidDataException("Vertex declaration has no position.");
                        var vertices = new ZoneClipper.Vertex[data.VertexCount];
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            var values = new List<float>();
                            foreach (int c in offsets.Keys.OrderBy(c => c)) values.AddRange(data.GetString(i, c, " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s, CultureInfo.InvariantCulture)));
                            var attr = values.ToArray();
                            Vector3 Read(int o) => new Vector3(attr[o], attr[o + 1], attr[o + 2]);
                            void Write(int o, Vector3 p) { attr[o] = p.X; attr[o + 1] = p.Y; attr[o + 2] = p.Z; }
                            var position = Vector3.TransformCoordinate(Read(offsets[0]), transform); Write(offsets[0], position);
                            if (offsets.TryGetValue(3, out int normal)) Write(normal, Vector3.Normalize(Vector3.TransformNormal(Read(normal), normalMatrix)));
                            if (offsets.TryGetValue(14, out int tangent)) Write(tangent, Vector3.Normalize(Vector3.TransformNormal(Read(tangent), transform)));
                            vertices[i] = new ZoneClipper.Vertex(position, attr);
                        }
                        var clipped = new List<ZoneClipper.Vertex>();
                        void Flush()
                        {
                            if (clipped.Count == 0) return;
                            var vd = new VertexData { Info = data.Info, VertexStride = data.Info.Stride, VertexType = data.VertexType }; vd.AllocateData(clipped.Count);
                            for (int i = 0; i < clipped.Count; i++)
                            {
                                var attr = clipped[i].Attributes;
                                foreach (var pair in offsets)
                                {
                                    var type = data.Info.GetComponentType(pair.Key);
                                    int count = VertexComponentTypes.GetComponentCount(type), offset = pair.Value;
                                    if ((pair.Key == 3 || pair.Key == 14) && count >= 3)
                                    {
                                        var n = new Vector3(attr[offset], attr[offset + 1], attr[offset + 2]);
                                        if (n.LengthSquared() > 1e-12f) { n.Normalize(); attr[offset] = n.X; attr[offset + 1] = n.Y; attr[offset + 2] = n.Z; }
                                    }
                                    var strings = new string[count];
                                    for (int j = 0; j < count; j++) strings[j] = type == VertexComponentType.Colour || type == VertexComponentType.UByte4
                                        ? Math.Max(0, Math.Min(255, (int)Math.Round(attr[offset + j]))).ToString(CultureInfo.InvariantCulture) : F(attr[offset + j]);
                                    int start = 0; vd.SetString(i, pair.Key, strings, ref start);
                                }
                            }
                            var min = clipped.Select(v => v.Position).Aggregate(Vector3.Min); var max = clipped.Select(v => v.Position).Aggregate(Vector3.Max);
                            geometries.Add(new DrawableGeometry { ShaderID = geom.ShaderID, VertexData = vd,
                                VertexBuffer = new VertexBuffer { Info = data.Info, Data1 = vd, Data2 = vd, VertexStride = data.Info.Stride, VertexCount = (uint)clipped.Count },
                                IndexBuffer = new IndexBuffer { Indices = Enumerable.Range(0, clipped.Count).Select(i => (ushort)i).ToArray(), IndicesCount = (uint)clipped.Count },
                                AABB = new AABB_s { Min = new Vector4(min, 0), Max = new Vector4(max, 0) } });
                            clipped.Clear();
                        }
                        var indices = geom.IndexBuffer.Indices;
                        for (int i = 0; i + 2 < indices.Length; i += 3)
                        {
                            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                            foreach (var triangle in clipper.Clip(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]))
                            { if (clipped.Count + 3 > 60000) Flush(); clipped.AddRange(triangle); }
                        }
                        Flush();
                    }
                    if (geometries.Count == 0) continue;
                    model.Geometries = geometries.ToArray();
                    RebuildModel(model); output.Add(model);
                }
                return output.Count == 0 ? null : output.ToArray();
            }
            draw.DrawableModels.High = ClipModels(draw.DrawableModels.High);
            draw.DrawableModels.Med = ClipModels(draw.DrawableModels.Med);
            draw.DrawableModels.Low = ClipModels(draw.DrawableModels.Low);
            draw.DrawableModels.VLow = ClipModels(draw.DrawableModels.VLow);
            draw.DrawableModels.Extra = ClipModels(draw.DrawableModels.Extra);
            var lights = new List<LightAttributes>();
            foreach (var light in draw.LightAttributes?.data_items ?? new LightAttributes[0])
            {
                light.Position = Vector3.TransformCoordinate(light.Position, transform);
                if (!clipper.Contains(light.Position)) continue;
                light.Direction = Vector3.TransformNormal(light.Direction, transform);
                light.Tangent = Vector3.TransformNormal(light.Tangent, transform);
                if (light.Direction.LengthSquared() > 0) light.Direction = Vector3.Normalize(light.Direction);
                if (light.Tangent.LengthSquared() > 0) light.Tangent = Vector3.Normalize(light.Tangent);
                if (light.CullingPlaneNormal.LengthSquared() > 0) Warn("Drawable light culling planes require review after recentering.");
                lights.Add(light);
            }
            draw.LightAttributes = new ResourceSimpleList64<LightAttributes> { data_items = lights.ToArray() };
            RefreshDrawable(draw);
        }

        private static void RebuildModel(DrawableModel model)
        {
            // ResourceBuilder reads BlockLength/GetParts BEFORE Write(), which is too late to fix these counts.
            // Clipping can remove geometries or split one geometry into several vertex-buffer chunks.
            int count = model.Geometries.Length;
            if (count > ushort.MaxValue) throw new InvalidDataException("Too many geometries in a clipped model.");
            model.GeometriesCount1 = model.GeometriesCount2 = model.GeometriesCount3 = (ushort)count;
            model.GeometryPointers = new ulong[count];
            var boxes = model.Geometries.Select(g => g.AABB).ToList();
            if (boxes.Count > 1) boxes.Insert(0, new AABB_s { Min = boxes.Select(b => b.Min).Aggregate(Vector4.Min), Max = boxes.Select(b => b.Max).Aggregate(Vector4.Max) });
            model.BoundsData = boxes.ToArray(); model.ShaderMapping = model.Geometries.Select(g => g.ShaderID).ToArray();
        }
        private static void RefreshDrawable(Drawable d)
        {
            d.BuildAllModels(); d.BuildRenderMasks(); d.BuildVertexDecls(); d.AssignGeometryShaders(d.ShaderGroup);
            var boxes = d.AllModels.SelectMany(m => m.Geometries).Select(g => g.AABB).ToArray();
            d.BoundingBoxMin = boxes.Length > 0 ? boxes.Select(b => b.Min.XYZ()).Aggregate(Vector3.Min) : Vector3.Zero;
            d.BoundingBoxMax = boxes.Length > 0 ? boxes.Select(b => b.Max.XYZ()).Aggregate(Vector3.Max) : Vector3.Zero;
            d.BoundingCenter = (d.BoundingBoxMin + d.BoundingBoxMax) * 0.5f;
            d.BoundingSphereRadius = (d.BoundingBoxMax - d.BoundingCenter).Length();
        }
        private Drawable Merge(List<Drawable> draws)
        {
            var merged = draws[0]; var shaders = new List<ShaderFX>(); var models = new List<DrawableModel>(); var textures = new List<Texture>();
            var variants = new Dictionary<string, List<Texture>>(StringComparer.OrdinalIgnoreCase);
            var reservedHashes = new HashSet<uint>(draws.SelectMany(d =>
                (d.ShaderGroup.TextureDictionary?.Textures?.data_items ?? new Texture[0]).Cast<TextureBase>()
                .Concat(TextureParameters(d).Select(p => (TextureBase)p.Data))).Select(t => JenkHash.GenHash(t.Name.ToLowerInvariant())));
            var usedHashes = new HashSet<uint>();
            int rename = 0;
            foreach (var d in draws)
            {
                // Capture old bindings before renaming: XML loading can share the texture object
                // between the dictionary and several shader parameters.
                var bindings = TextureParameters(d).Select(p => new { Parameter = p, Name = ((TextureBase)p.Data).Name }).ToArray();
                var replacements = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
                foreach (var texture in d.ShaderGroup.TextureDictionary?.Textures?.data_items ?? new Texture[0])
                {
                    string originalName = texture.Name;
                    if (!variants.TryGetValue(originalName, out var sameName)) variants[originalName] = sameName = new List<Texture>();
                    var target = sameName.FirstOrDefault(t => SameTexture(t, texture));
                    if (target == null)
                    {
                        uint hash = JenkHash.GenHash(originalName.ToLowerInvariant());
                        if (sameName.Count > 0 || usedHashes.Contains(hash))
                        {
                            string unique;
                            do { unique = "rq_tex_" + (++rename).ToString("D4"); hash = JenkHash.GenHash(unique); }
                            while (!reservedHashes.Add(hash));
                            texture.Name = unique;
                        }
                        texture.NameHash = hash; usedHashes.Add(hash);
                        target = texture; sameName.Add(target); textures.Add(target);
                    }
                    replacements[originalName] = target;
                }
                foreach (var binding in bindings)
                    if (replacements.TryGetValue(binding.Name, out var replacement)) binding.Parameter.Data = replacement;
                int offset = shaders.Count;
                shaders.AddRange(d.ShaderGroup.Shaders?.data_items ?? new ShaderFX[0]);
                if (shaders.Count > ushort.MaxValue) throw new InvalidDataException("Too many shaders in merged LOD.");
                // Map LOD resources normally put their geometry in High; retain the best available internal level.
                foreach (var m in d.DrawableModels.High ?? d.DrawableModels.Med ?? d.DrawableModels.Low ?? d.DrawableModels.VLow ?? new DrawableModel[0])
                { foreach (var g in m.Geometries) g.ShaderID = (ushort)(g.ShaderID + offset); RebuildModel(m); models.Add(m); }
            }
            var dict = new TextureDictionary(); dict.BuildFromTextureList(textures);
            merged.ShaderGroup = new ShaderGroup { Shaders = new ResourcePointerArray64<ShaderFX> { data_items = shaders.ToArray() }, TextureDictionary = dict };
            merged.DrawableModels = new DrawableModelsBlock { High = models.ToArray() };
            merged.LightAttributes = new ResourceSimpleList64<LightAttributes> { data_items = draws.SelectMany(d => d.LightAttributes?.data_items ?? new LightAttributes[0]).ToArray() };
            RefreshDrawable(merged); return merged;
        }

        private static IEnumerable<ShaderParameter> TextureParameters(Drawable draw) =>
            (draw.ShaderGroup.Shaders?.data_items ?? new ShaderFX[0]).SelectMany(s => s.ParametersList?.Parameters ?? new ShaderParameter[0])
                .Where(p => p.DataType == 0 && p.Data is TextureBase);

        private static bool SameTexture(Texture a, Texture b) =>
            a.Width == b.Width && a.Height == b.Height && a.Depth == b.Depth && a.Levels == b.Levels && a.Stride == b.Stride
            && a.Format == b.Format && a.UsageData == b.UsageData && a.ExtraFlags == b.ExtraFlags
            && (a.Data?.FullData ?? new byte[0]).SequenceEqual(b.Data?.FullData ?? new byte[0]);

        private void WriteAsset(string name, Drawable draw, YmapFile map, YtypFile types, CEntityDef original, string role, string source)
        {
            operation = "Writing " + name + ".ydr (source: " + source + ")";
            token.ThrowIfCancellationRequested(); JenkIndex.Ensure(name); draw.Name = name;
            var file = new YdrFile { Drawable = draw }; var bytes = file.Save(); var verify = new YdrFile(); verify.Load(bytes);
            if (verify.Drawable == null) throw new InvalidDataException("Drawable roundtrip failed: " + name);
            var expected = draw.AllModels; var actual = verify.Drawable.AllModels;
            if (expected.Length != actual.Length) throw new InvalidDataException("YDR model count changed during serialization: " + name);
            for (int m = 0; m < expected.Length; m++)
            {
                var eg = expected[m].Geometries; var ag = actual[m].Geometries;
                if (eg.Length != ag.Length) throw new InvalidDataException("YDR geometry count changed during serialization: " + name);
                for (int g = 0; g < eg.Length; g++)
                    if (eg[g].VertexData.VertexCount != ag[g].VertexData.VertexCount || eg[g].IndexBuffer.Indices.Length != ag[g].IndexBuffer.Indices.Length || eg[g].ShaderID != ag[g].ShaderID)
                        throw new InvalidDataException("YDR geometry buffers changed during serialization: " + name);
            }
            File.WriteAllBytes(Path.Combine(stage, name + ".ydr"), bytes);
            string textures = Path.Combine(stage, name); Directory.CreateDirectory(textures);
            File.WriteAllText(Path.Combine(stage, name + ".ydr.xml"), YdrXml.GetXml(file, textures));
            var definition = new CBaseArchetypeDef { name = JenkHash.GenHash(name), assetName = JenkHash.GenHash(name),
                assetType = rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE, bbMin = draw.BoundingBoxMin, bbMax = draw.BoundingBoxMax,
                bsCentre = draw.BoundingCenter, bsRadius = draw.BoundingSphereRadius, lodDist = Math.Max(200, original.lodDist), hdTextureDist = 15 };
            var archetype = new Archetype(); archetype.Init(types, ref definition); types.AddArchetype(archetype);
            var def = original; def.archetypeName = definition.name; def.position = Vector3.Zero; def.rotation = new Vector4(0, 0, 0, 1);
            def.scaleXY = def.scaleZ = 1; def.parentIndex = -1; def.numChildren = 0; def.extensions = new Array_StructurePointer(); def.flags &= ~8u;
            var ent = new YmapEntityDef(map, 0, ref def); ent.SetArchetype(archetype); map.AddEntity(ent);
            manifestAssets.Add("{\"file\":" + Q(name + ".ydr") + ",\"xml\":" + Q(name + ".ydr.xml") + ",\"role\":" + Q(role) + ",\"source\":" + Q(source) + "}"); result.Assets++;
        }

        private static YmapFile NewMap(string name)
        { JenkIndex.Ensure(name); var m = new YmapFile { Name = name + ".ymap", Loaded = true }; m._CMapData.name = JenkHash.GenHash(name); return m; }
        private void WriteMap(YmapFile map, string name)
        {
            operation = "Writing " + name + ".ymap";
            map.CalcFlags(); map.CalcExtents();
            if ((map.AllEntities?.Length ?? 0) == 0 && (map.OccludeModels?.Length ?? 0) == 0)
            { map._CMapData.entitiesExtentsMin = map._CMapData.entitiesExtentsMax = Vector3.Zero; map._CMapData.streamingExtentsMin = map._CMapData.streamingExtentsMax = Vector3.Zero; }
            var bytes = map.Save(); var verify = new YmapFile(); verify.Load(bytes);
            if ((verify.AllEntities?.Length ?? 0) != (map.AllEntities?.Length ?? 0) || (verify.OccludeModels?.Length ?? 0) != (map.OccludeModels?.Length ?? 0))
                throw new InvalidDataException("YMAP verification failed: " + name);
            File.WriteAllBytes(Path.Combine(stage, name + ".ymap"), bytes);
            File.WriteAllText(Path.Combine(stage, name + ".ymap.xml"), MetaXml.GetXml(verify, out string xmlName));
        }
        private static void BuildLodLinks(YmapFile map)
        {
            var entities = (map.AllEntities ?? new YmapEntityDef[0]).OrderByDescending(e => (int)e._CEntityDef.lodLevel == 5 ? 0 : (int)e._CEntityDef.lodLevel).ToArray();
            map.AllEntities = entities;
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i]; e.Index = i; int level = (int)e._CEntityDef.lodLevel; if (level == 5) level = 0;
                int parent = -1;
                for (int j = i - 1; j >= 0; j--) { int l = (int)entities[j]._CEntityDef.lodLevel; if (l != 5 && l > level) { parent = j; break; } }
                e._CEntityDef.parentIndex = parent; e._CEntityDef.flags &= ~8u;
                if (parent >= 0) { entities[parent]._CEntityDef.numChildren++; e.Parent = entities[parent]; }
            }
            map.RootEntities = entities.Where(e => e.Parent == null).ToArray();
        }

        private void ClipPropBounds(Asset asset)
        {
            var entity = asset.Entity;
            if ((entity._CEntityDef.flags & 4) != 0) { collisionReport.Add(entity.Name + ": embedded collisions disabled by placement flag 4."); return; }
            var transform = Matrix.Scaling(entity.Scale) * Matrix.RotationQuaternion(entity.Orientation) * Matrix.Translation(entity.Position - request.Pivot);
            int trianglesBefore = result.CollisionTriangles, nativeBefore = result.CollisionPrimitives, partsBefore = result.PropCollisionParts;
            foreach (var part in ZonePropCollisions.GetParts((DrawableBase)asset.FragmentDrawable ?? asset.Drawable))
            {
                result.PropCollisionParts++;
                ClipBounds(part.Bounds, part.Transform * transform, entity.Name, true, true);
            }
            collisionReport.Add(entity.Name + " at " + V(entity.Position) + ": " + (result.PropCollisionParts - partsBefore) + " source parts, "
                + (result.CollisionTriangles - trianglesBefore) + " triangles, " + (result.CollisionPrimitives - nativeBefore) + " native primitives.");
        }

        private void CopyCollisionMetadata(Bounds output, Bounds original, bool prop)
        {
            output.CopyFrom(original);
            output.CompositeFlags1 = original.CompositeFlags1; output.CompositeFlags2 = original.CompositeFlags2;
            // Fragment archetypes don't store the static-world composite filter flags.
            if (prop && output.CompositeFlags1.Flags1 == 0 && output.CompositeFlags1.Flags2 == 0
                && output.CompositeFlags2.Flags1 == 0 && output.CompositeFlags2.Flags2 == 0)
            {
                var flags = new BoundCompositeChildrenFlags {
                    Flags1 = EBoundCompositeFlags.MAP_WEAPON | EBoundCompositeFlags.MAP_DYNAMIC | EBoundCompositeFlags.MAP_ANIMAL | EBoundCompositeFlags.MAP_COVER | EBoundCompositeFlags.MAP_VEHICLE,
                    Flags2 = EBoundCompositeFlags.VEHICLE_NOT_BVH | EBoundCompositeFlags.VEHICLE_BVH | EBoundCompositeFlags.PED | EBoundCompositeFlags.RAGDOLL
                        | EBoundCompositeFlags.ANIMAL | EBoundCompositeFlags.ANIMAL_RAGDOLL | EBoundCompositeFlags.OBJECT | EBoundCompositeFlags.PLANT | EBoundCompositeFlags.PROJECTILE | EBoundCompositeFlags.EXPLOSION
                        | EBoundCompositeFlags.FORKLIFT_FORKS | EBoundCompositeFlags.TEST_WEAPON | EBoundCompositeFlags.TEST_CAMERA | EBoundCompositeFlags.TEST_AI
                        | EBoundCompositeFlags.TEST_SCRIPT | EBoundCompositeFlags.TEST_VEHICLE_WHEEL | EBoundCompositeFlags.GLASS };
                output.CompositeFlags1 = output.CompositeFlags2 = flags;
            }
        }

        // BoundBVH's base GetReferences rebuilds from polygon.BoxMin/Max, which use
        // GetVertexPos and thus Transform. Build these local boxes before restoring placement.
        private sealed class NativePrimitiveGeometry : BoundBVH
        {
            public override IResourceBlock[] GetReferences()
            {
                var transform = Transform; Transform = Matrix.Identity;
                try { return base.GetReferences(); }
                finally { Transform = transform; }
            }
        }

        private void PreservePolygons(BoundGeometry geometry, List<BoundPolygon> polygons, Matrix transform, bool prop)
        {
            // Split by material to stay below the byte-sized material table limit.
            foreach (var group in polygons.GroupBy(p => p.Material))
            {
                var originals = group.ToArray();
                for (int start = 0; start < originals.Length; start += 6000)
                {
                    var selected = originals.Skip(start).Take(6000).ToArray();
                    var indices = selected.SelectMany(p => p.VertexIndices).Distinct().ToArray();
                    var remap = indices.Select((id, i) => new { id, i }).ToDictionary(p => p.id, p => p.i);
                    var output = new NativePrimitiveGeometry { Type = BoundsType.GeometryBVH };
                    CopyCollisionMetadata(output, geometry, prop);
                    var positions = indices.Select(geometry.GetVertex).ToArray();
                    // Quantization is signed 16-bit, relative to CenterGeom. A subset's
                    // extent no longer covers offsets from the original district center.
                    var center = (positions.Aggregate(Vector3.Min) + positions.Aggregate(Vector3.Max)) * .5f;
                    output.CenterGeom = geometry.CenterGeom + center;
                    output.Vertices = positions.Select(p => p - center).ToArray();
                    output.VertexColours = geometry.VertexColours == null ? null : indices.Select(geometry.GetVertexColour).ToArray();
                    output.MaterialColours = geometry.MaterialColours?.ToArray();
                    var copies = new List<BoundPolygon>();
                    foreach (var polygon in selected)
                    {
                        BoundPolygon copy = polygon is BoundPolygonSphere ? (BoundPolygon)new BoundPolygonSphere() : polygon is BoundPolygonCapsule ? new BoundPolygonCapsule()
                            : polygon is BoundPolygonCylinder ? (BoundPolygon)new BoundPolygonCylinder() : new BoundPolygonBox();
                        using (var stream = new MemoryStream())
                        { using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) polygon.Write(writer); copy.Read(stream.ToArray(), 0); }
                        copy.VertexIndices = polygon.VertexIndices.Select(i => remap[i]).ToArray();
                        copy.Owner = output; copy.Index = copies.Count; copy.Material = polygon.Material;
                        copies.Add(copy);
                    }
                    output.Polygons = copies.ToArray(); output.BuildMaterials(); output.BuildBVH(false); output.CalculateQuantum();
                    output.Transform = transform; output.TransformInv = Matrix.Invert(transform);
                    collisionChildren.Add(output); result.CollisionPrimitives += copies.Count;
                }
            }
        }

        private void ClipBounds(Bounds bounds, Matrix parent, string source, bool transformIncluded = false, bool prop = false)
        {
            if (bounds == null) return;
            var transform = transformIncluded ? parent : bounds.Transform * parent;
            if (bounds is BoundComposite composite)
            { foreach (var child in composite.Children?.data_items ?? new Bounds[0]) ClipBounds(child, transform, source, false, prop); return; }
            var geometry = bounds as BoundGeometry;
            var shape = geometry == null ? ZoneCollisionGeometry.FromBound(bounds) : null;
            if (geometry == null && shape == null) { Warn(source + ": collision type " + bounds.Type + " is not supported."); return; }
            if (shape != null)
            {
                if (shape.IsOutside(clipper, transform)) return;
                if (shape.IsInside(clipper, transform))
                {
                    var copy = XmlYbn.GetYbn(YbnXml.GetXml(new YbnFile { Bounds = bounds })).Bounds;
                    CopyCollisionMetadata(copy, bounds, prop);
                    copy.Transform = transform; copy.TransformInv = Matrix.Invert(transform);
                    collisionChildren.Add(copy); result.CollisionPrimitives++; return;
                }
            }
            var verts = new List<Vector3>(); var materials = new List<BoundMaterial_s>();
            void Flush()
            {
                if (verts.Count == 0) return;
                // A separate bound per material avoids overflowing the byte material-index table.
                foreach (var group in Enumerable.Range(0, materials.Count).GroupBy(i => materials[i]))
                {
                    var positions = group.SelectMany(i => verts.Skip(i * 3).Take(3)).ToArray();
                    var center = (positions.Aggregate(Vector3.Min) + positions.Aggregate(Vector3.Max)) * 0.5f;
                    var output = new BoundBVH { Type = BoundsType.GeometryBVH, CenterGeom = center, Vertices = positions.Select(p => p - center).ToArray() };
                    CopyCollisionMetadata(output, bounds, prop);
                    output.MaterialColours = geometry?.MaterialColours?.ToArray();
                    var polys = new List<BoundPolygon>();
                    for (int i = 0; i < positions.Length; i += 3) polys.Add(new BoundPolygonTriangle { Type = BoundPolygonType.Triangle, Owner = output, Index = i / 3,
                        triIndex1 = (ushort)i, triIndex2 = (ushort)(i + 1), triIndex3 = (ushort)(i + 2), Material = group.Key, edgeIndex1 = 65535, edgeIndex2 = 65535, edgeIndex3 = 65535 });
                    output.Polygons = polys.ToArray(); output.CalculateMinMax(); output.BuildMaterials(); output.UpdateEdgeIndices(); output.UpdateTriangleAreas();
                    output.CalculateVertsShrunk(); output.CalculateQuantum(); output.BuildBVH(false);
                    collisionChildren.Add(output); result.CollisionTriangles += polys.Count;
                }
                verts.Clear(); materials.Clear();
            }
            void Emit(Vector3[] triangle, BoundMaterial_s material)
            { if (verts.Count + 3 > 30000) Flush(); verts.AddRange(triangle); materials.Add(material); }
            void CutPrimitive(ZoneCollisionGeometry.Shape primitive, BoundMaterial_s material)
            {
                int before = result.CollisionTriangles + materials.Count;
                foreach (var triangle in ZoneCollisionGeometry.Cut(primitive, transform, clipper)) { token.ThrowIfCancellationRequested(); Emit(triangle, material); }
                if (result.CollisionTriangles + materials.Count > before) result.CutCollisionPrimitives++;
            }
            if (shape != null)
            {
                var material = new BoundMaterial_s { Type = bounds.MaterialIndex, MaterialColorIndex = bounds.MaterialColorIndex, ProceduralId = bounds.ProceduralId,
                    RoomId = bounds.RoomId, PedDensity = bounds.PedDensity, Flags = (EBoundMaterialFlags)bounds.PolyFlags };
                CutPrimitive(shape, material); Flush(); return;
            }
            var preserved = new List<BoundPolygon>();
            foreach (var polygon in geometry.Polygons ?? new BoundPolygon[0])
            {
                token.ThrowIfCancellationRequested();
                if (!(polygon is BoundPolygonTriangle triangle))
                {
                    var primitive = ZoneCollisionGeometry.FromPolygon(geometry, polygon);
                    if (primitive == null) { Warn(source + ": collision polygon type " + polygon.Type + " is not supported."); continue; }
                    if (primitive.IsOutside(clipper, transform)) continue;
                    if (primitive.IsInside(clipper, transform)) preserved.Add(polygon);
                    else CutPrimitive(primitive, polygon.Material);
                    continue;
                }
                Vector3 P(int i) => Vector3.TransformCoordinate(geometry.GetVertex(i) + geometry.CenterGeom, transform);
                foreach (var t in clipper.Clip(new ZoneClipper.Vertex(P(triangle.vertIndex1)), new ZoneClipper.Vertex(P(triangle.vertIndex2)), new ZoneClipper.Vertex(P(triangle.vertIndex3))))
                {
                    var positions = t.Select(v => v.Position).ToArray();
                    if (transform.Determinant() < 0) { var swap = positions[1]; positions[1] = positions[2]; positions[2] = swap; }
                    Emit(positions, polygon.Material);
                }
            }
            Flush();
            PreservePolygons(geometry, preserved, transform, prop);
        }

        private void ClipOcclusion(IEnumerable<Vector3> source, uint flags, YmapFile map)
        {
            var points = source.ToArray(); var output = new List<Vector3>();
            void Flush()
            {
                if (output.Count == 0) return;
                var model = new YmapOccludeModel(map, new OccludeModel { flags = flags }) { Index = occluders.Count,
                    Vertices = output.ToArray(), Indices = Enumerable.Range(0, output.Count).Select(i => (byte)i).ToArray() };
                model.BuildTriangles(); model.BuildData(); occluders.Add(model); result.OcclusionTriangles += output.Count / 3; output.Clear();
            }
            for (int i = 0; i + 2 < points.Length; i += 3)
            {
                token.ThrowIfCancellationRequested();
                foreach (var triangle in clipper.Clip(new ZoneClipper.Vertex(points[i] - request.Pivot), new ZoneClipper.Vertex(points[i + 1] - request.Pivot), new ZoneClipper.Vertex(points[i + 2] - request.Pivot)))
                { if (output.Count + 3 > 255) Flush(); output.AddRange(triangle.Select(v => v.Position)); }
            }
            Flush();
        }
    }
}
