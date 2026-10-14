using CodeWalker.GameFiles;
using CodeWalker.World;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

// Replay a recorded zone against the original world YBNs, without modifying the
// game or the original package. Props and other assets are deliberately excluded.
static class ZoneRealCollisionTests
{
    public static void Run(string directory, string game, string output)
    {
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var m = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(directory,"manifest.json")));
        float[] Numbers(object value) => ((System.Collections.IEnumerable)value).Cast<object>().Select(Convert.ToSingle).ToArray();
        var p = Numbers(m["pivot_world"]);
        var request = new ZonePackageExporter.Request {
            Name = "real_collisions_" + Guid.NewGuid().ToString("N").Substring(0,8), OutputDirectory = output,
            Pivot = new Vector3(p[0],p[1],p[2]), MinZ = Convert.ToSingle(m["min_z_world"]), MaxZ = Convert.ToSingle(m["max_z_world"]),
            Polygon = ((System.Collections.IEnumerable)m["polygon_world"]).Cast<object>().Select(Numbers).Select(v=>new Vector2(v[0],v[1])).ToArray() };
        GTA5Keys.LoadFromPath(game);
        var manager = new RpfManager(); manager.Init(game,false,s=>{},Console.WriteLine,false,false);
        var names = File.ReadAllLines(Path.Combine(directory,"COLLISIONS.txt")).Where(s=>s.Contains(".ybn:") && !s.StartsWith("Output "))
            .Select(s=>s.Substring(0,s.IndexOf(':'))).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            var entries = manager.EntryDict.Values.OfType<RpfFileEntry>().Where(e=>string.Equals(e.Name,name,StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length != 1) throw new InvalidOperationException("Expected one unambiguous source for " + name + "; found " + entries.Length);
            var file = manager.GetFile<YbnFile>(entries[0]); request.Collisions.Add(file);
            Console.WriteLine("Source: " + entries[0].Path);
        }
        if (request.Collisions.Count == 0) throw new InvalidOperationException("No recorded world collision sources.");
        var result = new ZonePackageExporter(request,CancellationToken.None).Export(Console.WriteLine);
        Console.WriteLine("PASS: original zone replay and binary vertex verification. " + result.CollisionTriangles + " triangles, " + result.CollisionPrimitives + " primitives.");
        Console.WriteLine("Package: " + result.Directory);
    }
}
