using CodeWalker;
using CodeWalker.GameFiles;
using CodeWalker.World;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Web.Script.Serialization;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static ZoneClipper.Vertex P(float x, float y, float z = 0) => new ZoneClipper.Vertex(new Vector3(x, y, z), new[] { x, y });
    static Vector2[] Square = { new Vector2(-1,-1), new Vector2(1,-1), new Vector2(1,1), new Vector2(-1,1) };
    static float Area(IEnumerable<ZoneClipper.Vertex[]> triangles) => triangles.Sum(t => Vector3.Cross(t[1].Position - t[0].Position, t[2].Position - t[0].Position).Length() / 2);
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "--editor")
        { EditorControlTests(args[1]); Console.WriteLine("PASS: " + checks + " editor checks."); return; }
        if (args.Length > 1 && args[0] == "--validate-package")
        { ValidatePackage(args[1]); Console.WriteLine("PASS: " + checks + " package checks (read only)."); return; }
        if (args.Length > 3 && args[0] == "--replay-world-collisions")
        { ZoneRealCollisionTests.Run(args[1], args[2], args[3]); return; }
        if (args.Length > 2 && args[0] == "--audit-lods")
        { ZoneRealLodTests.Audit(args[1], args[2], args.Length > 3 ? args[3] : null); return; }
        if (args.Length > 1 && args[0] == "--validate-slod")
        { ZoneRealLodTests.Validate(args[1]); return; }
        var zone = new ZoneClipper(Square, -1, 1);
        var crossing = zone.Clip(P(-5,-5), P(5,-5), P(0,8));
        Check(Math.Abs(Area(crossing) - 4) < 0.0001, "Triangle with no vertex inside must cover the entire square.");
        foreach (var v in crossing.SelectMany(t => t))
        {
            Check(zone.Contains(v.Position), "Output outside polygon.");
            Check(Math.Abs(v.Attributes[0] - v.Position.X) < 0.0001 && Math.Abs(v.Attributes[1] - v.Position.Y) < 0.0001, "UV interpolation failed.");
        }
        var reversed = new ZoneClipper(Square.Reverse(), -1, 1);
        Check(Math.Abs(Area(reversed.Clip(P(-5,-5), P(5,-5), P(0,8))) - 4) < 0.0001, "Winding changed output.");
        var concave = new ZoneClipper(new[] { new Vector2(0,0), new Vector2(2,0), new Vector2(2,1), new Vector2(1,1), new Vector2(1,2), new Vector2(0,2) }, -1, 1);
        Check(Math.Abs(Area(concave.Clip(P(-10,-10), P(10,-10), P(0,20))) - 3) < 0.0001, "Concave clipping area failed.");
        Check(!concave.Contains(new Vector3(1.5f,1.5f,0)), "Concave notch filled.");
        Check(zone.Clip(P(-1,-1,5), P(1,-1,5), P(0,1,5)).Count == 0, "Z rejection failed.");
        var height = zone.Clip(P(-0.5f,0,-5), P(0.5f,0,5), P(0.5f,0,-5));
        Check(height.Count > 0 && height.SelectMany(t => t).All(v => zone.Contains(v.Position)), "Z clipping failed.");
        bool rejected = false;
        try { new ZoneClipper(new[] { new Vector2(0,0),new Vector2(1,1),new Vector2(0,1),new Vector2(1,0) },0,1); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Self-intersection accepted.");
        var diagonal = zone.Clip(P(-0.8f,-0.8f,-0.5f),P(0.8f,0.8f,-0.5f),P(0,0,0.5f));
        Check(Math.Abs(Area(diagonal) - 1.1313708f) < 0.0001, "Internal diagonal duplicated a vertical surface.");
        EditorMathTests();
        ResourceLoaderTests().GetAwaiter().GetResult();
        DrawableLoaderTests().GetAwaiter().GetResult();
        if (args.Length > 0)
        {
            var output = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "CodeWalkerZoneTests");
            ExportRoundtrip(args[0], output);
            ExportGeometryCountRegression(args[0], output);
            ExportTextureConflictRegression(args[0], output);
            ExportReferenceOnly(output);
            FinalizationTests(output);
            ZoneCollisionTests.Run(args[0], output, Check);
        }
        Console.WriteLine("PASS: " + checks + " checks.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    static SafeFileHandle LockDirectory(string path)
    {
        // Open a real directory without FILE_SHARE_DELETE, reproducing Windows' rename refusal.
        var handle = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return handle;
    }

    static void FinalizationTests(string output)
    {
        string root = Path.Combine(output, "finalization_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(root);
        string Stage(string name) { string p = Path.Combine(root, name + ".partial"); Directory.CreateDirectory(p); File.WriteAllText(Path.Combine(p, "data.txt"), "intact"); return p; }
        string dest = Path.Combine(root, "released"), stage = Stage("released");
        using (var handle = LockDirectory(stage))
        {
            bool blocked = false;
            try { Directory.Move(stage, dest); } catch (IOException) { blocked = true; } catch (UnauthorizedAccessException) { blocked = true; }
            Check(blocked, "The Windows directory lock did not reproduce the failure.");
            int retries = 0;
            Check(ZonePackageFinalizer.TryRename(stage, dest, CancellationToken.None, s => { retries++; handle.Dispose(); }), "Rename was not retried after the lock was released.");
            Check(retries > 0 && File.ReadAllText(Path.Combine(dest, "data.txt")) == "intact", "Retried rename lost files.");
        }
        stage = Stage("cancelled"); dest = Path.Combine(root, "cancelled");
        using (var handle = LockDirectory(stage))
        using (var cancel = new CancellationTokenSource())
        {
            bool cancelled = false;
            try { ZonePackageFinalizer.TryRename(stage, dest, cancel.Token, s => cancel.Cancel()); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && Directory.Exists(stage) && !Directory.Exists(dest), "Cancellation during rename wait lost the package.");
        }
        stage = Stage("occupied"); dest = Path.Combine(root, "occupied"); Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "keep.txt"), "existing");
        bool refused = false;
        try { ZonePackageFinalizer.TryRename(stage, dest, CancellationToken.None); } catch (IOException) { refused = true; }
        Check(refused && File.ReadAllText(Path.Combine(dest, "keep.txt")) == "existing" && Directory.Exists(stage), "Existing destination was overwritten.");

        var request = new ZonePackageExporter.Request { Name = "locked", OutputDirectory = root, Polygon = Square, MinZ = -1, MaxZ = 1,
            ImporterPath = Path.Combine(root, "importer.py") };
        File.WriteAllText(request.ImporterPath, "# test importer");
        var def = new CEntityDef { archetypeName = 123, rotation = new Vector4(0,0,0,1), scaleXY = 1, scaleZ = 1 };
        request.Assets.Add(new ZonePackageExporter.Asset { Entity = new YmapEntityDef(new YmapFile(), 0, ref def) });
        SafeFileHandle persistent = null;
        try
        {
            var result = new ZonePackageExporter(request, CancellationToken.None).Export(s =>
            {
                if (persistent == null) persistent = LockDirectory(Directory.GetDirectories(root, "locked.partial-*").Single());
            });
            Check(result.FinalizationNotice != null && Directory.Exists(result.Directory), "Permanent rename refusal incorrectly failed the export.");
            Check(File.Exists(Path.Combine(result.Directory, "FINALIZATION.txt")) && !File.Exists(Path.Combine(result.Directory, "FAILED.txt")), "Completed export incorrectly marked as failed.");
            Check(File.ReadAllText(Path.Combine(result.Directory, "import_zone_package.py")) == "# test importer", "Importer missing when finalization is blocked.");
            CheckResourcePairs(result.Directory);
        }
        finally { persistent?.Dispose(); }
    }

    static void ValidatePackage(string directory)
    {
        directory = Path.GetFullPath(directory);
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var manifest = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        Check((string)manifest["schema"] == "ragequit.zone-package", "Unknown package schema.");
        string FilePath(string name)
        {
            Check(!string.IsNullOrWhiteSpace(name) && Path.GetFileName(name) == name, "Invalid package filename.");
            string path = Path.Combine(directory, name);
            Check(File.Exists(path), "Missing package file: " + name);
            return path;
        }
        var assets = ((System.Collections.IEnumerable)manifest["assets"]).Cast<Dictionary<string, object>>().ToArray();
        foreach (var asset in assets)
        {
            string path = FilePath((string)asset["file"]);
            var binary = new YdrFile(); binary.Load(File.ReadAllBytes(path));
            var xml = XmlYdr.GetYdr(File.ReadAllText(FilePath((string)asset["xml"])), Path.Combine(directory, Path.GetFileNameWithoutExtension(path)));
            var bm = binary.Drawable.AllModels; var xm = xml.Drawable.AllModels;
            Check(bm.Length == xm.Length, "YDR model count mismatch: " + path);
            for (int m = 0; m < bm.Length; m++)
            {
                Check(bm[m].Geometries.Length == xm[m].Geometries.Length, "YDR geometry count mismatch.");
                for (int g = 0; g < bm[m].Geometries.Length; g++)
                {
                    var b = bm[m].Geometries[g]; var x = xm[m].Geometries[g];
                    Check(b.VertexData.VertexCount == x.VertexData.VertexCount && b.IndexBuffer.Indices.SequenceEqual(x.IndexBuffer.Indices) && b.ShaderID == x.ShaderID, "YDR buffer mismatch.");
                }
            }
            var bt = binary.Drawable.ShaderGroup.TextureDictionary; var xt = xml.Drawable.ShaderGroup.TextureDictionary;
            foreach (var texture in bt?.Textures?.data_items ?? new Texture[0])
            {
                var other = xt?.Lookup(texture.NameHash);
                Check(other?.Data != null && texture.Data.FullData.SequenceEqual(other.Data.FullData), "Missing or altered XML/DDS texture: " + texture.Name);
            }
            Console.WriteLine(Path.GetFileName(path) + ": binary/XML geometry and textures verified.");
        }
        foreach (string key in new[] { "entities", "occlusion" })
        {
            string path = FilePath((string)manifest[key]);
            var binary = new YmapFile(); binary.Load(File.ReadAllBytes(path));
            var doc = new System.Xml.XmlDocument(); doc.Load(FilePath((string)manifest[key] + ".xml"));
            var xml = new YmapFile(); xml.Load(XmlMeta.GetRSCData(doc));
            var be = binary.AllEntities ?? new YmapEntityDef[0]; var xe = xml.AllEntities ?? new YmapEntityDef[0];
            Check(be.Length == xe.Length, "YMAP entity count mismatch.");
            for (int i = 0; i < be.Length; i++)
                Check(be[i]._CEntityDef.archetypeName == xe[i]._CEntityDef.archetypeName && (be[i].Position - xe[i].Position).Length() < .001f, "YMAP placement mismatch.");
            int bc = (binary.OccludeModels ?? new YmapOccludeModel[0]).Sum(m => m.Indices.Length / 3);
            int xc = (xml.OccludeModels ?? new YmapOccludeModel[0]).Sum(m => m.Indices.Length / 3);
            Check(bc == xc, "YMAP occlusion mismatch.");
            if (key == "entities") Check(be.Length == assets.Length + ((System.Collections.IEnumerable)manifest["references"]).Cast<object>().Count(), "Manifest placements missing from YMAP.");
            Console.WriteLine(Path.GetFileName(path) + ": " + be.Length + " entities, " + bc + " occlusion triangles.");
        }
        string ytypPath = FilePath((string)manifest["name"] + ".ytyp");
        var ytyp = new YtypFile(); ytyp.Load(File.ReadAllBytes(ytypPath));
        var typeDoc = new System.Xml.XmlDocument(); typeDoc.Load(FilePath((string)manifest["name"] + ".ytyp.xml"));
        var ytypXml = new YtypFile(); ytypXml.Load(XmlMeta.GetRSCData(typeDoc));
        Check(ytyp.AllArchetypes.Length == assets.Length && ytypXml.AllArchetypes.Length == assets.Length, "YTYP definitions missing.");
        var collisionFiles = manifest.TryGetValue("collisions", out var layers)
            ? ((System.Collections.IEnumerable)layers).Cast<Dictionary<string, object>>().Select(l => (string)l["file"]).ToArray()
            : manifest["collision"] is string single ? new[] { single } : new string[0];
        int collisionTriangles = 0, collisionPrimitives = 0;
        foreach (var collision in collisionFiles)
        {
            var ybn = new YbnFile(); ybn.Load(File.ReadAllBytes(FilePath(collision)));
            var ybnXml = XmlYbn.GetYbn(File.ReadAllText(FilePath(collision + ".xml")));
            ZonePackageExporter.VerifyCollisionCopy(ybnXml.Bounds,ybn.Bounds);
            int Count(Bounds bounds, bool triangles) => bounds == null ? 0 : bounds is BoundComposite c ? (c.Children?.data_items ?? new Bounds[0]).Sum(b=>Count(b,triangles))
                : bounds is BoundGeometry g ? (g.Polygons ?? new BoundPolygon[0]).Count(p=>(p is BoundPolygonTriangle)==triangles) : triangles ? 0 : 1;
            int tris = Count(ybn.Bounds,true), primitives = Count(ybn.Bounds,false);
            Check(tris == Count(ybnXml.Bounds,true) && primitives == Count(ybnXml.Bounds,false), "YBN binary/XML collision count mismatch.");
            collisionTriangles += tris; collisionPrimitives += primitives;
            Console.WriteLine(collision + ": " + tris + " collision triangles, " + primitives + " native primitives.");
        }
        if (manifest.TryGetValue("collision_stats", out var statsObject))
        {
            var stats = (Dictionary<string,object>)statsObject;
            Check(Convert.ToInt32(stats["triangles"]) == collisionTriangles && Convert.ToInt32(stats["native_primitives"]) == collisionPrimitives, "Manifest collision totals mismatch.");
        }
    }

    static async Task ResourceLoaderTests()
    {
        // Simulate the viewport filling all ten cache queue slots before a large scan begins.
        var queue = new Queue<YmapFile>(Enumerable.Range(0,10).Select(i=>new YmapFile()));
        var current = new Dictionary<uint,YmapFile>();
        var calls = new Dictionary<uint,int>();
        var evicted = new YmapFile();
        int maxOutstanding=0, progressCalls=0;
        YmapFile Request(uint hash)
        {
            calls[hash]=calls.TryGetValue(hash,out int count)?count+1:1;
            if(hash==999) return null;
            if(hash==3 && calls[hash]==1) return evicted; // the original reference will never load
            if(!current.TryGetValue(hash,out var file)) current[hash]=file=new YmapFile();
            if(!file.Loaded && queue.Count<10 && !queue.Contains(file)) queue.Enqueue(file);
            maxOutstanding=Math.Max(maxOutstanding,current.Values.Count(f=>!f.Loaded));
            return file;
        }
        var hashes=Enumerable.Range(1,40).Select(i=>(uint)i).Concat(new uint[]{1,999}).ToArray();
        var loaded=await ZoneResourceLoader.LoadAsync(hashes,Request,h=>"test_"+h+".ymap",
            (done,total,file)=>{progressCalls++; Check(total==41 && done<=total,"Invalid scan progress."); if(queue.Count>0) queue.Dequeue().Loaded=true;},
            CancellationToken.None,timeoutMilliseconds:5000,pollMilliseconds:1);
        Check(loaded.Files.Count==40 && loaded.Files.Values.All(f=>f.Loaded),"Saturated queue left resources unloaded.");
        Check(loaded.Missing.SequenceEqual(new uint[]{999}),"Missing resources not reported accurately.");
        Check(calls.Values.Any(c=>c>1),"Deferred requests were not retried.");
        Check(!evicted.Loaded && !ReferenceEquals(loaded.Files[3],evicted),"Loader kept waiting on an evicted object.");
        Check(maxOutstanding<=4,"Scan overloaded the bounded request window.");
        Check(progressCalls>10,"Queue saturation was not exercised.");

        bool timeout=false;
        try
        {
            await ZoneResourceLoader.LoadAsync(new uint[]{42},h=>new YbnFile(),h=>"problem_collision.ybn",null,
                CancellationToken.None,timeoutMilliseconds:15,pollMilliseconds:1);
        }
        catch(TimeoutException ex) { timeout=ex.Message.Contains("problem_collision.ybn"); }
        Check(timeout,"Timeout did not identify the failing resource.");

        var cancellation=new CancellationTokenSource(); int requests=0; bool cancelled=false;
        try
        {
            await ZoneResourceLoader.LoadAsync(new uint[]{1,2,3,4,5},h=>{requests++; return new YmapFile();},h=>h.ToString(),
                (done,total,file)=>cancellation.Cancel(),cancellation.Token,pollMilliseconds:1);
        }
        catch(OperationCanceledException) { cancelled=true; }
        Check(cancelled && requests<=4,"Scan cancellation kept requesting resources.");

        var ready=new YtdFile {Loaded=true};
        var textures=await ZoneResourceLoader.LoadAsync(new uint[]{7,7},h=>ready,h=>"textures.ytd",null,CancellationToken.None);
        Check(textures.Files.Count==1 && ReferenceEquals(textures.Files[7],ready),"Already loaded texture dictionary not reused.");
    }

    static async Task DrawableLoaderTests()
    {
        JenkIndex.Ensure("delayed_slod");
        var arche = new Archetype { Hash = JenkHash.GenHash("delayed_slod"),
            _BaseArchetypeDef = new CBaseArchetypeDef { name = JenkHash.GenHash("delayed_slod") } };
        var expected = new Drawable();
        var stale = new YdrFile(); var ready = new YdrFile { Drawable = expected };
        int requests = 0;
        var loaded = await ZoneResourceLoader.LoadDrawableAsync(arche, h => null,
            h => { requests++; if (requests == 1) return stale; ready.Loaded = requests >= 5; return ready; },
            h => { throw new Exception("Pending YDR incorrectly fell back to YFT"); }, null, CancellationToken.None,
            timeoutMilliseconds: 5000, pollMilliseconds: 1);
        Check(ReferenceEquals(loaded, expected) && requests >= 5 && !stale.Loaded, "Delayed/evicted SLOD was omitted.");
        bool timeout = false;
        try { await ZoneResourceLoader.LoadDrawableAsync(arche, h => null, h => new YdrFile(), h => null,
            null, CancellationToken.None, timeoutMilliseconds: 15, pollMilliseconds: 1); }
        catch (TimeoutException ex) { timeout = ex.Message.Contains("delayed_slod.ydr"); }
        Check(timeout, "Unloaded LOD must stop export and identify its file.");
        arche.DrawableDict = 42;
        var ydd = new YddFile { Loaded = true, Dict = new Dictionary<uint, Drawable> { { arche.Hash, expected } } };
        loaded = await ZoneResourceLoader.LoadDrawableAsync(arche, h => ydd,
            h => { throw new Exception("Loaded YDD ignored"); }, h => null, null, CancellationToken.None);
        Check(ReferenceEquals(loaded, expected), "YDD LOD selection failed.");
        ydd.Dict.Clear(); bool missingEntry = false;
        try { await ZoneResourceLoader.LoadDrawableAsync(arche, h => ydd, h => null, h => null, null, CancellationToken.None); }
        catch (InvalidDataException ex) { missingEntry = ex.Message.Contains("delayed_slod"); }
        Check(missingEntry, "Loaded dictionary missing its LOD entry was silently accepted.");
        loaded = await ZoneResourceLoader.LoadDrawableAsync(arche, h => null, h => new YdrFile { Loaded = true, Drawable = expected }, h => null, null, CancellationToken.None);
        Check(ReferenceEquals(loaded, expected), "Missing YDD did not fall back to standalone YDR.");
        loaded = await ZoneResourceLoader.LoadDrawableAsync(arche, h => null, h => null, h => null, null, CancellationToken.None);
        Check(loaded == null, "Absent files confused with pending loading.");
        var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false;
        try { await ZoneResourceLoader.LoadDrawableAsync(arche, h => ydd, h => null, h => null, null, cancel.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Drawable loading ignored cancellation.");
    }

    static void EditorControlTests(string path)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(path));
        var type = assembly.GetType("CodeWalker.Project.Panels.RageQuitZonePanel", true);
        using (var panel = (IDisposable)Activator.CreateInstance(type, new object[] { null }))
        {
            object Call(string name, params object[] values) => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(panel, values);
            Vector3[] Points() => (Vector3[])type.GetProperty("Outline").GetValue(panel);
            bool Valid() { var state=type.GetProperty("Visual").GetValue(panel); return (bool)state.GetType().GetField("Valid").GetValue(state); }
            var seed = Square.Select(p=>new Vector3(p,12)).ToArray();
            Call("SetPoints", (object)seed);
            Check(Valid(),"Valid editor polygon rejected.");
            Call("SelectPoint",1);
            Call("BeginDrag");
            var snapshot=type.GetProperty("Visual").GetValue(panel);
            Call("MovePoint",1,new Vector3(2,-1,15));
            Check(Points()[1]==new Vector3(2,-1,15),"Handle did not update XYZ.");
            Check(((Vector3[])snapshot.GetType().GetField("Points").GetValue(snapshot))[1]==seed[1],"Render snapshot mutated during edit.");
            Check(Valid(),"Dragged valid polygon became invalid.");
            Call("EndDrag",false); Call("UndoPoint");
            Check(Points().SequenceEqual(seed),"Drag undo did not restore points.");
            Call("SelectPoint",1); Call("BeginDrag"); Call("MovePoint",1,new Vector3(0,2,12));
            Check(!Valid(),"Self-crossing polygon must invalidate fill/export.");
            Call("EndDrag",true);
            Check(Points().SequenceEqual(seed),"Cancelled drag changed polygon.");
            Call("InsertPoint",0,new Vector3(0,-1,12));
            Check(Points().Length==5 && Valid(),"Edge insertion failed.");
            Call("DeletePoint");
            Check(Points().SequenceEqual(seed),"Delete selected point changed other vertices.");
            Call("UndoPoint"); Check(Points().Length==5,"Delete undo failed.");
            type.GetProperty("Drawing").GetSetMethod(true).Invoke(panel,new object[]{true});
            Call("AddPoint",new Vector3(5,6,27));
            Check(Points().Last()==new Vector3(5,6,27),"Surface click height was projected back onto the old drawing plane.");
        }
    }

    static void EditorMathTests()
    {
        var triangles = new List<Vector3>();
        void Rect(float x, float y, float w, float h)
        { var a=new Vector3(x,y,0); var b=new Vector3(x+w,y,0); var c=new Vector3(x+w,y+h,0); var d=new Vector3(x,y+h,0); triangles.AddRange(new[]{a,b,c,a,c,d}); }
        Rect(0,0,4,1); Rect(0,1,1,3);
        var footprint = ZoneGeometry.TraceFootprint(triangles.ToArray(), .05f, CancellationToken.None);
        var shape = new ZoneClipper(footprint.Points, -1, 1);
        Check(!footprint.BridgedIslands, "Connected L contour unexpectedly convexified.");
        Check(shape.Contains(new Vector3(.5f,3,0)) && shape.Contains(new Vector3(3,.5f,0)), "Mesh contour misses arms.");
        Check(!shape.Contains(new Vector3(2,2,0)), "Concave mesh contour lost its notch.");
        Check(footprint.Points.Length <= 256, "Contour exceeds editor point budget.");
        var shift = new Vector3(4000,-6000,75);
        var translated = ZoneGeometry.TraceFootprint(triangles.Select(p=>p+shift).ToArray(), .05f, CancellationToken.None);
        var translatedShape = new ZoneClipper(translated.Points, 74, 76);
        Check(!translatedShape.Contains(shift+new Vector3(2,2,0)), "Far-world contour lost precision.");
        Rect(10,0,1,1);
        var islands = ZoneGeometry.TraceFootprint(triangles.ToArray(), .1f, CancellationToken.None);
        Check(islands.BridgedIslands && new ZoneClipper(islands.Points,-1,1).Contains(new Vector3(10.5f,.5f,0)), "Separate selections not enclosed/reported.");
        var cancellation = new CancellationTokenSource(); cancellation.Cancel(); bool cancelled=false;
        try { ZoneGeometry.TraceFootprint(triangles.ToArray(), .1f, cancellation.Token); } catch(OperationCanceledException) { cancelled=true; }
        Check(cancelled,"Contour cancellation ignored.");
        var pos = new Vector3(100,200,300);
        var view = Matrix.LookAtRH(Vector3.Zero, Vector3.Normalize(new Vector3(1,2,-3)), Vector3.UnitZ);
        foreach(bool ortho in new[]{false,true})
        {
            var projection = ortho ? Matrix.OrthoRH(80,60,10000,.01f) : Matrix.PerspectiveFovRH(1.1f,4f/3,10000,.01f);
            var vp=view*projection; var inverse=Matrix.Invert(vp);
            foreach(var pixel in new[]{new Vector2(400,300),new Vector2(75,510),new Vector2(725,42)})
            {
                var ray=ZoneGeometry.ClickRay(inverse,pos,ortho,pixel.X,pixel.Y,800,600);
                var hit=ray.Position+ray.Direction*100;
                var ndc=Vector3.TransformCoordinate(hit-pos,vp);
                var reproject=new Vector2((ndc.X+1)*400,(1-ndc.Y)*300);
                Check((reproject-pixel).Length()<.02f,"Click ray not aligned with the clicked pixel.");
                Check(Math.Abs(ray.Direction.Length()-1)<.0001f,"World ray not normalized.");
            }
        }
    }

    static void ExportGeometryCountRegression(string fixture, string parent)
    {
        string folder = Path.Combine(Path.GetDirectoryName(fixture), Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fixture)));
        Drawable Read() => XmlYdr.GetYdr(File.ReadAllText(fixture), folder).Drawable;
        void SetGeometry(Drawable drawable, DrawableGeometry[] geometries)
        {
            var model = drawable.AllModels[0]; model.Geometries = geometries;
            model.GeometriesCount1 = model.GeometriesCount2 = model.GeometriesCount3 = (ushort)geometries.Length;
            model.ShaderMapping = geometries.Select(g => g.ShaderID).ToArray();
            var boxes = geometries.Select(g => g.AABB).ToList();
            if(boxes.Count > 1) boxes.Insert(0,new AABB_s {Min=boxes.Select(b=>b.Min).Aggregate(Vector4.Min),Max=boxes.Select(b=>b.Max).Aggregate(Vector4.Max)});
            model.BoundsData=boxes.ToArray();
            drawable.DrawableModels = new DrawableModelsBlock { High = new[] { model } }; drawable.BuildAllModels();
        }
        void ExportCase(string name, Drawable drawable, Vector3 position, int expectedGeometries, int? expectedIndices = null)
        {
            string before=YdrXml.GetXml(new YdrFile {Drawable=drawable});
            var def=new CEntityDef {archetypeName=123,position=position,rotation=new Vector4(0,0,0,1),scaleXY=1,scaleZ=1,lodLevel=(rage__eLodType)1,parentIndex=-1,lodDist=500};
            var request=new ZonePackageExporter.Request {Name=name+"_"+Guid.NewGuid().ToString("N").Substring(0,8),OutputDirectory=parent,Pivot=Vector3.Zero,Polygon=Square,MinZ=-.5f,MaxZ=.5f};
            request.Assets.Add(new ZonePackageExporter.Asset {Entity=new YmapEntityDef(new YmapFile(),0,ref def),Drawable=drawable});
            var result=new ZonePackageExporter(request,CancellationToken.None).Export();
            Check(result.Assets==1,"Geometry-count regression lost its drawable.");
            var reload=new YdrFile(); reload.Load(File.ReadAllBytes(Directory.GetFiles(result.Directory,"*.ydr").Single()));
            var model=reload.Drawable.AllModels.Single();
            Check(model.Geometries.Length==expectedGeometries,"Clipping did not change the geometry count as expected.");
            Check(model.GeometriesCount1==expectedGeometries && model.GeometriesCount2==expectedGeometries && model.GeometriesCount3==expectedGeometries,"Serialized geometry counts are stale.");
            Check(model.BoundsData.Length==(expectedGeometries>1?expectedGeometries+1:expectedGeometries) && model.ShaderMapping.Length==expectedGeometries,"Geometry bounds/shader table length mismatch.");
            if(expectedIndices.HasValue) Check(model.Geometries.Sum(g=>g.IndexBuffer.Indices.Length)==expectedIndices.Value,"Chunked geometry lost triangles in binary roundtrip.");
            Check(before==YdrXml.GetXml(new YdrFile {Drawable=drawable}),"Geometry-count rebuild mutated the source.");
        }
        var shrinking=Read(); var distant=Read().AllModels[0].Geometries[0];
        for(int i=0;i<distant.VertexData.VertexCount;i++)
        {
            var p=distant.VertexData.GetVector3(i,0)+new Vector3(50,0,0); int offset=0;
            distant.VertexData.SetString(i,0,new[]{FloatUtil.ToString(p.X),FloatUtil.ToString(p.Y),FloatUtil.ToString(p.Z)},ref offset);
        }
        distant.AABB = new AABB_s { Min=distant.AABB.Min+new Vector4(50,0,0,0), Max=distant.AABB.Max+new Vector4(50,0,0,0) };
        SetGeometry(shrinking,new[]{shrinking.AllModels[0].Geometries[0],distant});
        ExportCase("geometry_removed",shrinking,new Vector3(.75f,0,0),1);

        var growing=Read(); var geometry=growing.AllModels[0].Geometries[0];
        var smallTriangle=new[]{new Vector3(.2f,-.6f,0),new Vector3(.3f,-.6f,0),new Vector3(.2f,-.5f,0)};
        for(int i=0;i<3;i++)
        { var p=smallTriangle[i]; int offset=0; geometry.VertexData.SetString(i,0,new[]{FloatUtil.ToString(p.X),FloatUtil.ToString(p.Y),FloatUtil.ToString(p.Z)},ref offset); }
        geometry.IndexBuffer.Indices=Enumerable.Range(0,60003).Select(i=>(ushort)(i%3)).ToArray(); geometry.IndexBuffer.IndicesCount=60003;
        SetGeometry(growing,new[]{geometry});
        ExportCase("geometry_split",growing,Vector3.Zero,2,60003);
    }

    static void ExportRoundtrip(string fixture, string parent)
    {
        var source = XmlYdr.GetYdr(File.ReadAllText(fixture), Path.Combine(Path.GetDirectoryName(fixture), Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fixture)))) .Drawable;
        var transform = Matrix.Scaling(.75f,2,1.6f)*Matrix.RotationYawPitchRoll(.4f,.3f,.7f)*Matrix.Translation(100,200,10);
        var top = (source.BoundingBoxMin+source.BoundingBoxMax)*.5f; top.Z=source.BoundingBoxMax.Z;
        var ray = new Ray(Vector3.TransformCoordinate(top+new Vector3(0,0,10),transform),Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ,transform)));
        float hitDistance=ZoneGeometry.Raycast(source,transform,ray,float.MaxValue);
        Check(Math.Abs(hitDistance-16)<.001f,"Raycast failed on rotated, nonuniformly scaled mesh.");
        float farther=ZoneGeometry.Raycast(source,transform*Matrix.Translation(ray.Direction*5),ray,float.MaxValue);
        Check(Math.Abs(farther-21)<.001f,"Raycast world-space distance wrong.");
        Check(ZoneGeometry.Raycast(source,transform*Matrix.Translation(ray.Direction*5),ray,hitDistance)==hitDistance,"Farther surface replaced nearest hit.");
        source.LightAttributes = new ResourceSimpleList64<LightAttributes> { data_items = new[] {
            new LightAttributes { Position = Vector3.Zero, Direction = -Vector3.UnitZ, Tangent = Vector3.UnitX, Type = LightType.Point, Falloff = 5, Intensity = 1 },
            new LightAttributes { Position = new Vector3(20,0,0), Direction = -Vector3.UnitZ, Tangent = Vector3.UnitX, Type = LightType.Point, Falloff = 5, Intensity = 1 }
        } };
        string before = YdrXml.GetXml(new YdrFile { Drawable = source });
        var pivot = new Vector3(100,200,10);
        var request = new ZonePackageExporter.Request { Name = "zone_test_" + Guid.NewGuid().ToString("N").Substring(0,8), OutputDirectory = parent, Pivot = pivot,
            Polygon = Square.Select(p => p + new Vector2(pivot.X, pivot.Y)).ToArray(), MinZ = pivot.Z - 0.5f, MaxZ = pivot.Z + 0.5f };
        Directory.CreateDirectory(parent);
        foreach (int level in new[] { 0, 0, 1, 1, 2 })
        {
            var q = Quaternion.RotationAxis(Vector3.UnitZ, 0.35f);
            JenkIndex.Ensure("sollumz_cube");
            var def = new CEntityDef { archetypeName = JenkHash.GenHash("sollumz_cube"), position = pivot + new Vector3(0.75f,0,0), rotation = new Vector4(q.X,q.Y,q.Z,q.W), scaleXY = 0.75f, scaleZ = 1, lodLevel = (rage__eLodType)level, parentIndex = -1, lodDist = 500 };
            var entity = new YmapEntityDef(new YmapFile(), 0, ref def);
            request.Assets.Add(new ZonePackageExporter.Asset { Entity = entity, Drawable = source });
        }
        var collision = new BoundGeometry { Type = BoundsType.Geometry, CenterGeom = pivot, Vertices = new[] {new Vector3(-5,-5,0),new Vector3(5,-5,0),new Vector3(0,8,0)} };
        collision.Polygons = new BoundPolygon[] { new BoundPolygonTriangle { Owner = collision, triIndex1=0,triIndex2=1,triIndex3=2, Type = BoundPolygonType.Triangle, Material = new BoundMaterial_s() } };
        request.Collisions.Add(new YbnFile { Name = "test.ybn", Bounds = collision });
        var occmap = new YmapFile { Name = "source_occl.ymap" };
        var occ = new YmapOccludeModel(occmap, new OccludeModel()) { Vertices = collision.Vertices.Select(v => v + pivot).ToArray(), Indices = new byte[] {0,1,2} };
        occ.BuildTriangles(); occ.BuildData(); occmap.OccludeModels = new[] {occ}; request.Maps.Add(occmap);
        var result = new ZonePackageExporter(request,CancellationToken.None).Export(Console.WriteLine);
        Console.WriteLine("Package: " + result.Directory);
        Check(result.Assets == 2 && result.References == 2, "Expected two original HD references, one merged LOD, one SLOD1.");
        Check(!Directory.GetFiles(result.Directory,"*_hd_*.ydr*").Any(), "HD resources were unnecessarily copied.");
        Check(before == YdrXml.GetXml(new YdrFile { Drawable = source }), "Source drawable mutated.");
        Check(result.CollisionTriangles > 0 && result.OcclusionTriangles > 0, "Collision/occlusion missing.");
        var local = new ZoneClipper(Square,-0.5f,0.5f);
        foreach (string path in Directory.GetFiles(result.Directory,"*.ydr"))
        {
            var ydr = new YdrFile(); ydr.Load(File.ReadAllBytes(path));
            Check(ydr.Drawable != null, "Invalid YDR.");
            var lights = ydr.Drawable.LightAttributes.data_items;
            Check(lights.Length == (path.EndsWith("_lod.ydr") ? 2 : 1), "Drawable light selection/merge failed.");
            Check(lights.All(l => (l.Position - new Vector3(0.75f,0,0)).Length() < 0.0001f), "Light not recentered.");
            foreach (var g in ydr.Drawable.AllModels.SelectMany(m=>m.Geometries))
                for(int i=0;i<g.VertexData.VertexCount;i++) Check(local.Contains(g.VertexData.GetVector3(i,0)), "Exported YDR vertex outside zone.");
        }
        var ybn = new YbnFile(); ybn.Load(File.ReadAllBytes(Path.Combine(result.Directory, request.Name+".ybn")));
        Check(ybn.Bounds.SphereRadius > 0 && ybn.Bounds.BoxMax.X > ybn.Bounds.BoxMin.X, "Collision root has empty broad-phase bounds.");
        foreach(var child in ((BoundComposite)ybn.Bounds).Children.data_items.Cast<BoundGeometry>())
            for(int i=0;i<child.Vertices.Length;i++)
            { var p=child.GetVertexPos(i); Check(p.X >= -1.001 && p.X <= 1.001 && p.Y >= -1.001 && p.Y <= 1.001 && Math.Abs(p.Z)<0.001, "YBN coordinates/quantization failed."); }
        var map = new YmapFile(); map.Load(File.ReadAllBytes(Path.Combine(result.Directory,request.Name+".ymap")));
        var omap = new YmapFile(); omap.Load(File.ReadAllBytes(Path.Combine(result.Directory,request.Name+"_occl.ymap")));
        Check((map.OccludeModels?.Length??0)==0 && (omap.AllEntities?.Length??0)==0,"YMAP roles mixed.");
        Check(map.AllEntities.Length == 4 && omap.OccludeModels.Length>0,"YMAP roundtrip incomplete.");
        Check(map.AllEntities.Where(e=>!ZonePackageExporter.IsHd(e)).All(e=>e.Position==Vector3.Zero),"Generated LOD not centered.");
        foreach(var e in map.AllEntities.Where(ZonePackageExporter.IsHd))
        {
            var original=request.Assets[0].Entity;
            Check(e._CEntityDef.archetypeName==original._CEntityDef.archetypeName,"HD archetype renamed.");
            Check(e.Position==original.Position-pivot && e._CEntityDef.rotation==original._CEntityDef.rotation && e.Scale==original.Scale,"HD placement not preserved relative to pivot.");
        }
        foreach(var m in omap.OccludeModels) Check(m.Vertices.All(local.Contains),"Occluder outside zone.");
        Check(map.AllEntities.Where(e=>e._CEntityDef.parentIndex>=0).All(e=>e._CEntityDef.parentIndex<e.Index),"LOD parent ordering invalid.");
        var ytyp=new YtypFile(); ytyp.Load(File.ReadAllBytes(Path.Combine(result.Directory,request.Name+".ytyp")));
        Check(ytyp.AllArchetypes.Length==2,"YTYP should only define generated LODs.");
        Check(File.Exists(Path.Combine(result.Directory,"manifest.json")),"Manifest missing.");
        CheckResourcePairs(result.Directory);
    }

    static void CheckResourcePairs(string directory)
    {
        foreach(var file in Directory.GetFiles(directory).Where(f=>new[]{".ydr",".ymap",".ytyp",".ybn"}.Contains(Path.GetExtension(f))))
        {
            Check(File.Exists(file+".xml"),"Missing XML companion for "+file);
            var doc=new System.Xml.XmlDocument(); doc.Load(file+".xml");
            Check(doc.DocumentElement!=null,"Invalid resource XML: "+file);
        }
    }

    static void ExportReferenceOnly(string parent)
    {
        var pivot=new Vector3(100,200,10);
        var request=new ZonePackageExporter.Request {Name="references_"+Guid.NewGuid().ToString("N").Substring(0,8),OutputDirectory=parent,Pivot=pivot,
            Polygon=Square.Select(p=>p+new Vector2(pivot.X,pivot.Y)).ToArray(),MinZ=9,MaxZ=11};
        foreach(int level in new[]{0,5})
        {
            // Explicit manual additions outside the zone must remain; no drawable is required.
            var def=new CEntityDef {archetypeName=(uint)(123+level),position=pivot+new Vector3(20,3,4),rotation=new Vector4(0,0,.5f,.8660254f),scaleXY=2,scaleZ=3,lodLevel=(rage__eLodType)level,parentIndex=42,numChildren=3};
            request.Assets.Add(new ZonePackageExporter.Asset {Entity=new YmapEntityDef(new YmapFile(),0,ref def)});
        }
        var result=new ZonePackageExporter(request,CancellationToken.None).Export();
        Check(result.References==2 && result.Assets==0,"Reference-only export failed.");
        Check(!Directory.GetFiles(result.Directory,"*.ydr*").Any() && !Directory.Exists(Path.Combine(result.Directory,"textures")),"Reference-only package serialized HD resources.");
        var map=new YmapFile(); map.Load(File.ReadAllBytes(Path.Combine(result.Directory,request.Name+".ymap")));
        Check(map.AllEntities.Length==2 && map.AllEntities.All(e=>e.Position==new Vector3(20,3,4)),"Manual HD reference lost or transformed incorrectly.");
        Check(map.AllEntities.All(e=>e._CEntityDef.parentIndex==-1 && e._CEntityDef.numChildren==0),"Stale source LOD indices retained.");
        Check(request.Assets.All(a=>a.Entity._CEntityDef.parentIndex==42 && a.Entity._CEntityDef.numChildren==3 && a.Entity.Position.X==120),"Source entity was modified.");
        CheckResourcePairs(result.Directory);
    }

    static void ExportTextureConflictRegression(string fixture,string parent)
    {
        string folder=Path.Combine(Path.GetDirectoryName(fixture),Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fixture)));
        var request=new ZonePackageExporter.Request {Name="texture_conflict_"+Guid.NewGuid().ToString("N").Substring(0,8),OutputDirectory=parent,
            Polygon=Square,MinZ=-.5f,MaxZ=.5f};
        var payloads=new List<byte[]>(); var originals=new List<string>();
        for(int i=0;i<4;i++)
        {
            var draw=XmlYdr.GetYdr(File.ReadAllText(fixture),folder).Drawable;
            var texture=draw.ShaderGroup.TextureDictionary.Textures.data_items.Single();
            texture.Name=i==3?"rq_tex_0001":"prop_lod"; texture.NameHash=JenkHash.GenHash(texture.Name);
            if(i==1 || i==2) texture.Data.FullData[0]^=127;
            draw.ShaderGroup.TextureDictionary.BuildFromTextureList(new List<Texture>{texture});
            var parameter=draw.ShaderGroup.Shaders.data_items[0].ParametersList.Parameters.First(p=>p.DataType==0);
            parameter.Data=i==1?new TextureBase {Name=texture.Name,NameHash=texture.NameHash}:texture;
            payloads.Add(texture.Data.FullData.ToArray()); originals.Add(YdrXml.GetXml(new YdrFile{Drawable=draw}));
            var def=new CEntityDef {archetypeName=123,position=new Vector3(.5f,0,0),rotation=new Vector4(0,0,0,1),scaleXY=1,scaleZ=1,lodLevel=(rage__eLodType)1};
            request.Assets.Add(new ZonePackageExporter.Asset {Entity=new YmapEntityDef(new YmapFile(),0,ref def),Drawable=draw});
        }
        var result=new ZonePackageExporter(request,CancellationToken.None).Export();
        var path=Path.Combine(result.Directory,request.Name+"_lod.ydr");
        var binary=new YdrFile(); binary.Load(File.ReadAllBytes(path));
        var xml=XmlYdr.GetYdr(File.ReadAllText(path+".xml"),Path.Combine(result.Directory,request.Name+"_lod"));
        foreach(var file in new[]{binary,xml})
        {
            var dict=file.Drawable.ShaderGroup.TextureDictionary;
            Check(dict.Textures.data_items.Length==3,"Identical textures not deduplicated or conflicting texture lost.");
            Check(dict.Textures.data_items.Select(t=>t.NameHash).Distinct().Count()==3,"Conflicting texture hashes after merge.");
            Check(dict.Textures.data_items.Any(t=>t.Name=="rq_tex_0001") && dict.Textures.data_items.Any(t=>t.Name=="rq_tex_0002"),"Generated texture name overwrote an existing source name.");
            for(int i=0;i<4;i++)
            {
                var tex=(TextureBase)file.Drawable.ShaderGroup.Shaders.data_items[i].ParametersList.Parameters.First(p=>p.DataType==0).Data;
                Check(dict.Lookup(JenkHash.GenHash(tex.Name)).Data.FullData.SequenceEqual(payloads[i]),"Material points to the wrong texture after merge.");
                Check(file.Drawable.AllModels[i].Geometries.All(g=>g.ShaderID==i),"Geometry points to the wrong shader after merge.");
            }
        }
        for(int i=0;i<4;i++) Check(originals[i]==YdrXml.GetXml(new YdrFile{Drawable=request.Assets[i].Drawable}),"Texture conflict repair mutated a source resource.");
        CheckResourcePairs(result.Directory);
    }
}
