using CodeWalker.GameFiles;
using CodeWalker.World;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

static class ZoneRealLodTests
{
    public static void Audit(string directory, string game, string output = null)
    {
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var manifest = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        float[] Numbers(object value) => ((System.Collections.IEnumerable)value).Cast<object>().Select(Convert.ToSingle).ToArray();
        var points = ((System.Collections.IEnumerable)manifest["polygon_world"]).Cast<object>().Select(Numbers).ToArray();
        var min = new Vector3(points.Min(p => p[0]), points.Min(p => p[1]), Convert.ToSingle(manifest["min_z_world"]));
        var max = new Vector3(points.Max(p => p[0]), points.Max(p => p[1]), Convert.ToSingle(manifest["max_z_world"]));
        var pivot = Numbers(manifest["pivot_world"]);
        var export = new ZonePackageExporter.Request { Name = "slod_replay_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            OutputDirectory = output, Pivot = new Vector3(pivot[0], pivot[1], pivot[2]),
            Polygon = points.Select(p => new Vector2(p[0], p[1])).ToArray(), MinZ = min.Z, MaxZ = max.Z };
        bool Intersects(Vector3 a, Vector3 b) => a.X <= max.X && b.X >= min.X && a.Y <= max.Y && b.Y >= min.Y && a.Z <= max.Z && b.Z >= min.Z;
        GTA5Keys.LoadFromPath(game);
        var cache = new GameFileCache(2L * 1024 * 1024 * 1024, 60, game, false, "", false, "");
        Console.WriteLine("Initializing game cache for LOD audit...");
        cache.Init(s => { }, Console.WriteLine);
        using (var cancel = new CancellationTokenSource())
        {
            var worker = Task.Run(() => { while (!cancel.IsCancellationRequested) { cache.ContentThreadProc(); Thread.Sleep(1); } });
            try
            {
                foreach (var entry in cache.YmapDict.Values.Where(e => e.Name.StartsWith("hw1", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name))
                {
                    var map = cache.RpfMan.GetFile<YmapFile>(entry);
                    map.InitYmapEntityArchetypes(cache);
                    foreach (var entity in map.AllEntities ?? new YmapEntityDef[0])
                    {
                        if (ZonePackageExporter.IsHd(entity) || !Intersects(entity.BBMin, entity.BBMax)) continue;
                        var arche = entity.Archetype;
                        Console.WriteLine("Candidate " + entry.Name + " #" + entity.Index + " " + entity.Name + " level=" + entity._CEntityDef.lodLevel
                            + " parent=" + entity._CEntityDef.parentIndex + " dict=" + arche?.DrawableDict + " asset=" + arche?._BaseArchetypeDef.assetName
                            + " archetype=" + arche?.Hash + " box=" + entity.BBMin + " .. " + entity.BBMax);
                        var draw = ZoneResourceLoader.LoadDrawableAsync(arche, cache.GetYdd, cache.GetYdr, cache.GetYft,
                            null, CancellationToken.None).GetAwaiter().GetResult();
                        Console.WriteLine("  Drawable=" + (draw as Drawable)?.Name + " shaders=" + string.Join(",", draw?.ShaderGroup?.Shaders?.data_items?.Select(s => s.FileName.ToString()) ?? new string[0]));
                        if (draw == null && arche != null && arche.DrawableDict != 0)
                        {
                            var dict = cache.GetYdd(arche.DrawableDict);
                            Console.WriteLine("  YDD=" + dict?.RpfFileEntry?.Path + " loaded=" + dict?.Loaded + " keys=" + string.Join(",", dict?.Dict?.Select(kv => kv.Key + ":" + kv.Value.Name) ?? new string[0]));
                        }
                        if (output != null && entity._CEntityDef.lodLevel == rage__eLodType.LODTYPES_DEPTH_SLOD3)
                        {
                            if (!(draw is Drawable drawable)) throw new InvalidOperationException("Missing static SLOD3: " + entity.Name);
                            var asset = new ZonePackageExporter.Asset { Entity = entity, Drawable = drawable };
                            var textureHashes = new List<uint>(); var seen = new HashSet<uint>(); uint txdHash = arche.TextureDict;
                            while (txdHash != 0 && seen.Add(txdHash))
                            { textureHashes.Add(txdHash); txdHash = cache.TryGetParentYtdHash(txdHash); }
                            var textures = ZoneResourceLoader.LoadAsync(textureHashes, cache.GetYtd, h => h.ToString(), null, CancellationToken.None).GetAwaiter().GetResult();
                            foreach (var texture in drawable.ShaderGroup.Shaders.data_items.SelectMany(s => s.ParametersList?.Parameters ?? new ShaderParameter[0])
                                .Where(p => p.DataType == 0).Select(p => p.Data as TextureBase).Where(t => t != null))
                            {
                                uint hash = JenkHash.GenHash(texture.Name.ToLowerInvariant());
                                var resolved = drawable.ShaderGroup.TextureDictionary?.Lookup(hash) ?? textures.Files.Values.Select(t => t.TextureDict?.Lookup(hash)).FirstOrDefault(t => t != null);
                                if (resolved == null)
                                {
                                    var fallback = ZoneResourceLoader.LoadAsync(new[] { hash }, cache.TryGetTextureDictForTexture, h => texture.Name, null, CancellationToken.None).GetAwaiter().GetResult();
                                    resolved = fallback.Files.Values.Select(t => t.TextureDict?.Lookup(hash)).FirstOrDefault(t => t != null);
                                }
                                if (resolved == null) throw new InvalidOperationException("Missing replay texture: " + texture.Name);
                                asset.Textures.Add(resolved);
                            }
                            export.Assets.Add(asset);
                        }
                    }
                }
            }
            finally { cancel.Cancel(); worker.Wait(); }
        }
        if (output != null)
        {
            if (!export.Assets.Any(a => a.Entity.Name == "hw1_lod_slod4")) throw new InvalidOperationException("Main SLOD3 missing from replay.");
            var result = new ZonePackageExporter(export, CancellationToken.None).Export(Console.WriteLine);
            Validate(result.Directory);
            Console.WriteLine("Package: " + result.Directory);
        }
    }

    public static void Validate(string directory)
    {
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var manifest = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        float[] Numbers(object value) => ((System.Collections.IEnumerable)value).Cast<object>().Select(Convert.ToSingle).ToArray();
        var pivot = Numbers(manifest["pivot_world"]);
        var polygon = ((System.Collections.IEnumerable)manifest["polygon_world"]).Cast<object>().Select(Numbers)
            .Select(p => new Vector2(p[0] - pivot[0], p[1] - pivot[1]));
        var zone = new ZoneClipper(polygon, Convert.ToSingle(manifest["min_z_world"]) - pivot[2], Convert.ToSingle(manifest["max_z_world"]) - pivot[2]);
        var cells = zone.GetTriangles();
        // Distances in metres, evaluated in double precision. Contains() uses
        // unnormalised cross products, unsuitable for verifying float roundtrips
        // on the long edges of this 106-point real contour.
        double OutsideDistance(Vector3 p)
        {
            double xy = cells.Min(t => Enumerable.Range(0, 3).Max(i => {
                var a = t[i]; var b = t[(i + 1) % 3];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                return -(dx * ((double)p.Y - a.Y) - dy * ((double)p.X - a.X)) / Math.Sqrt(dx * dx + dy * dy);
            }));
            return Math.Max(0, Math.Max(xy, Math.Max(zone.MinZ - p.Z, p.Z - zone.MaxZ)));
        }
        var path = Path.Combine(directory, manifest["name"] + "_slod3.ydr");
        var binary = new YdrFile(); binary.Load(File.ReadAllBytes(path));
        var xml = XmlYdr.GetYdr(File.ReadAllText(path + ".xml"), Path.Combine(directory, Path.GetFileNameWithoutExtension(path)));
        int binaryCount = 0;
        foreach (var file in new[] { binary, xml })
        {
            int opaqueTriangles = 0, totalTriangles = 0; double outside = 0;
            foreach (var model in file.Drawable.AllModels)
                foreach (var geometry in model.Geometries)
                {
                    int triangles = geometry.IndexBuffer.Indices.Length / 3;
                    string shader = file.Drawable.ShaderGroup.Shaders.data_items[geometry.ShaderID].FileName.ToString();
                    totalTriangles += triangles;
                    if (!shader.Contains("emissive")) opaqueTriangles += triangles;
                    for (int i = 0; i < geometry.VertexData.VertexCount; i++)
                        outside = Math.Max(outside, OutsideDistance(geometry.VertexData.GetVector3(i, 0)));
                }
            if (outside > .0001) throw new InvalidOperationException("SLOD3 vertex outside recorded zone: " + outside + " m.");
            if (opaqueTriangles == 0) throw new InvalidOperationException("Replay produced only emissive geometry.");
            if (ReferenceEquals(file, binary)) binaryCount = totalTriangles;
            else if (totalTriangles != binaryCount) throw new InvalidOperationException("SLOD3 binary/XML triangle count mismatch.");
            Console.WriteLine("PASS: " + (ReferenceEquals(file, binary) ? "binary" : "XML") + " SLOD3 replay, " + totalTriangles + " triangles including "
                + opaqueTriangles + " non-emissive triangles; maximum contour roundoff " + outside + " m (limit 0.0001 m).");
        }
    }
}
