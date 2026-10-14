using CodeWalker.GameFiles;
using CodeWalker.World;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

static class ZoneCollisionTests
{
    static Action<bool, string> Check;
    static readonly Vector2[] Square = { new Vector2(-1,-1), new Vector2(1,-1), new Vector2(1,1), new Vector2(-1,1) };
    static readonly BoundMaterial_s Material = new BoundMaterial_s { Type = 17, ProceduralId = 12, RoomId = 3, PedDensity = 2, MaterialColorIndex = 4, Flags = EBoundMaterialFlags.FLAG_NOT_CLIMBABLE | EBoundMaterialFlags.FLAG_NO_CAM_COLLISION_ALLOW_CLIPPING };

    static Bounds Primitive(BoundsType type)
    {
        var b = Bounds.Create(type); b.Type = type; b.SphereRadius = .4f; b.Margin = .15f;
        b.BoxMin = new Vector3(-.4f); b.BoxMax = new Vector3(.4f); b.MaterialIndex = 17; b.ProceduralId = 12; b.RoomId = 3; b.PedDensity = 2; b.MaterialColorIndex = 4;
        var shape = ZoneCollisionGeometry.FromBound(b);
        b.BoxMin = new Vector3(shape.Support(-Vector3.UnitX, Matrix.Identity).X, shape.Support(-Vector3.UnitY, Matrix.Identity).Y, shape.Support(-Vector3.UnitZ, Matrix.Identity).Z);
        b.BoxMax = new Vector3(shape.Support(Vector3.UnitX, Matrix.Identity).X, shape.Support(Vector3.UnitY, Matrix.Identity).Y, shape.Support(Vector3.UnitZ, Matrix.Identity).Z);
        return b;
    }

    static BoundGeometry Poly(BoundPolygonType type)
    {
        var g = new BoundBVH { Type = BoundsType.GeometryBVH, CenterGeom = new Vector3(.1f,.2f,0), Margin = .04f,
            MaterialColours = new[] { new BoundMaterialColour { R = 12, G = 23, B = 34, A = 255 } } };
        BoundPolygon p;
        switch (type)
        {
            case BoundPolygonType.Sphere: g.Vertices = new[] { Vector3.Zero }; p = new BoundPolygonSphere { sphereIndex = 0, sphereRadius = .4f }; break;
            case BoundPolygonType.Capsule: g.Vertices = new[] { new Vector3(0,-.2f,0), new Vector3(0,.2f,0) }; p = new BoundPolygonCapsule { capsuleIndex1 = 0, capsuleIndex2 = 1, capsuleRadius = .2f }; break;
            case BoundPolygonType.Cylinder: g.Vertices = new[] { new Vector3(0,-.3f,0), new Vector3(0,.3f,0) }; p = new BoundPolygonCylinder { cylinderIndex1 = 0, cylinderIndex2 = 1, cylinderRadius = .3f }; break;
            default:
                g.Vertices = new[] { new Vector3(-.4f,-.4f,-.4f), new Vector3(-.4f,.4f,.4f), new Vector3(.4f,.4f,-.4f), new Vector3(.4f,-.4f,.4f) };
                p = new BoundPolygonBox { boxIndex1 = 0, boxIndex2 = 1, boxIndex3 = 2, boxIndex4 = 3 }; break;
        }
        p.Owner = g; p.Material = Material; g.Polygons = new[] { p }; g.BuildMaterials(); g.BuildBVH(false); g.CalculateQuantum(); return g;
    }

    static ZonePackageExporter.Result Export(string parent, string label, Bounds bounds, float size = 1, ZonePackageExporter.Asset prop = null)
    {
        var request = new ZonePackageExporter.Request { Name = label + "_" + Guid.NewGuid().ToString("N").Substring(0,8), OutputDirectory = parent,
            Polygon = Square.Select(p => p * size).ToArray(), MinZ = -size, MaxZ = size };
        if (bounds != null) request.Collisions.Add(new YbnFile { Bounds = bounds, Name = label });
        var def = new CEntityDef { archetypeName = 123, rotation = new Vector4(0,0,0,1), scaleXY = 1, scaleZ = 1 };
        request.Assets.Add(prop ?? new ZonePackageExporter.Asset { Entity = new YmapEntityDef(new YmapFile(),0,ref def) });
        return new ZonePackageExporter(request, CancellationToken.None).Export();
    }
    static YbnFile Reload(ZonePackageExporter.Result result, bool xml)
    {
        string path = Directory.GetFiles(result.Directory, "*.ybn").Single();
        if (xml) return XmlYbn.GetYbn(File.ReadAllText(path + ".xml"));
        var file = new YbnFile(); file.Load(File.ReadAllBytes(path)); return file;
    }
    static void CompareSupport(ZoneCollisionGeometry.Shape expected, Matrix transform, Bounds child)
    {
        var shape = child is BoundGeometry g ? ZoneCollisionGeometry.FromPolygon(g, g.Polygons.Single()) : ZoneCollisionGeometry.FromBound(child);
        foreach (var d in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitZ, Vector3.Normalize(new Vector3(1,2,3)) })
            Check((expected.Support(d,transform) - shape.Support(d,child.Transform)).Length() < .002f, "Native primitive shape/transform changed in serialization.");
    }
    static void CheckCut(ZonePackageExporter.Result result)
    {
        Check(result.CutCollisionPrimitives == 1 && result.CollisionTriangles > 0 && result.CollisionPrimitives == 0, "Crossing primitive not converted/clipped.");
        foreach (bool xml in new[] {false,true})
        {
            var root = (BoundComposite)Reload(result, xml).Bounds;
            var tris = root.Children.data_items.Cast<BoundGeometry>().SelectMany(g => g.Polygons.Cast<BoundPolygonTriangle>().Select(p => new[] {p.Vertex1,p.Vertex2,p.Vertex3})).ToArray();
            Check(tris.SelectMany(t => t).All(p => Math.Abs(p.X) < 1.002f && Math.Abs(p.Y) < 1.002f && Math.Abs(p.Z) < 1.002f), "Cut collision exceeds zone.");
            var areaVector = tris.Select(t => Vector3.Cross(t[1]-t[0],t[2]-t[0])).Aggregate(Vector3.Zero,(a,b)=>a+b);
            Check(areaVector.Length() < .02f, "Cut primitive has missing caps or inconsistent winding: " + areaVector);
            double volume = tris.Sum(t => Vector3.Dot(t[0],Vector3.Cross(t[1],t[2]))/6.0);
            Check(volume > .0001 && volume <= 8.02, "Cut volume invalid: " + volume);
            Check(root.Children.data_items.Cast<BoundGeometry>().All(g => g.Polygons.All(p => p.Material.Type.Index == 17 && p.Material.ProceduralId == 12)), "Collision material lost.");
        }
    }

    public static void Run(string fixture, string parent, Action<bool,string> check)
    {
        Check = check;
        ExportLayers(parent);
        ExportWorldCoordinatePrimitives(parent);
        foreach (var type in new[] { BoundsType.Box, BoundsType.Sphere, BoundsType.Capsule, BoundsType.Cylinder, BoundsType.Disc })
        {
            var b = Primitive(type); b.Transform = Matrix.Scaling(1.2f,.8f,1.1f) * Matrix.RotationZ(.43f) * Matrix.Translation(2,3,1);
            string before = YbnXml.GetXml(new YbnFile { Bounds = b });
            var result = Export(parent,"native_" + type.ToString().ToLowerInvariant(),b,10);
            Check(result.CollisionPrimitives == 1 && result.CollisionTriangles == 0, "Contained primitive was unnecessarily triangulated: " + type);
            foreach (bool xml in new[] {false,true})
            {
                var root = (BoundComposite)Reload(result,xml).Bounds; var child = root.Children.data_items.Single();
                Check(child.Type == type && child.MaterialIndex == 17, "Native bound type/material changed.");
                CompareSupport(ZoneCollisionGeometry.FromBound(b),b.Transform,child);
                Check(root.BoxMin.X > 1 && root.BoxMax.Y > 3, "Root bounds ignore native child placement.");
            }
            Check(before == YbnXml.GetXml(new YbnFile { Bounds = b }), "Native preservation mutated its source.");
            b.Transform = Matrix.RotationZ(.21f) * Matrix.Translation(.9f,0,0);
            CheckCut(Export(parent,"cut_" + type.ToString().ToLowerInvariant(),b));
        }
        foreach (var type in new[] { BoundPolygonType.Box, BoundPolygonType.Sphere, BoundPolygonType.Capsule, BoundPolygonType.Cylinder })
        {
            var g = Poly(type); g.Transform = Matrix.Scaling(1.2f,.8f,1.1f) * Matrix.RotationZ(.43f) * Matrix.Translation(2,3,1);
            // Real map BVHs use a shared center for a large district. A retained subset
            // can be far from it even when its own extent is small.
            var offset = new Vector3(45,-60,18);
            g.Vertices = g.Vertices.Select(v => v + offset).ToArray(); g.CenterGeom -= offset;
            string before = YbnXml.GetXml(new YbnFile { Bounds = g });
            var result = Export(parent,"native_poly_" + type.ToString().ToLowerInvariant(),g,10);
            Check(result.CollisionPrimitives == 1 && result.CollisionTriangles == 0, "Contained polygon primitive was triangulated.");
            foreach (bool xml in new[] {false,true})
            {
                var child = (BoundGeometry)((BoundComposite)Reload(result,xml).Bounds).Children.data_items.Single();
                Check(child.Polygons.Single().Type == type && child.Polygons.Single().Material.Equals(Material), "Native polygon type/material lost.");
                CompareSupport(ZoneCollisionGeometry.FromPolygon(g,g.Polygons.Single()),g.Transform,child);
            }
            Check(before == YbnXml.GetXml(new YbnFile { Bounds = g }), "Source polygon mutated.");
            g.Transform = Matrix.RotationZ(.21f) * Matrix.Translation(.8f,0,0);
            CheckCut(Export(parent,"cut_poly_" + type.ToString().ToLowerInvariant(),g));
        }
        var huge = Primitive(BoundsType.Box); huge.BoxMin = new Vector3(-5); huge.BoxMax = new Vector3(5);
        CheckCut(Export(parent,"surrounding_box",huge));
        var outside = Primitive(BoundsType.Sphere); outside.Transform = Matrix.Translation(20,0,0);
        var omitted = Export(parent,"outside",outside);
        Check(omitted.CollisionPrimitives == 0 && omitted.CollisionTriangles == 0 && !Directory.GetFiles(omitted.Directory,"*.ybn").Any(), "Outside primitive exported.");
        var notch = new ZoneClipper(new[] {new Vector2(-2,-2),new Vector2(2,-2),new Vector2(2,2),new Vector2(.2f,2),new Vector2(0,0),new Vector2(-.2f,2),new Vector2(-2,2)},-2,2);
        var box = new ZoneCollisionGeometry.Shape { Type = ZoneCollisionGeometry.Kind.Box, A = new Vector3(-1,-1,-1), X = new Vector3(2,0,0), Y = new Vector3(0,2,0), Z = new Vector3(0,0,2) };
        Check(!box.IsInside(notch,Matrix.Identity), "Concave notch misclassified a crossing primitive as inside.");
        Check(ZoneCollisionGeometry.Cut(box,Matrix.Identity,notch).SelectMany(t=>t).All(notch.Contains), "Concave clipping leaks outside the polygon.");

        var localBound = Primitive(BoundsType.Sphere); localBound.Transform = Matrix.Translation(99,99,99);
        var physRoot = new BoundComposite { Type = BoundsType.Composite, Children = new ResourcePointerArray64<Bounds> { data_items = new[]{localBound} } };
        var bone = new Bone { Tag = 42, BindTransformInv = Matrix.Invert(Matrix.Translation(1,2,3)) };
        var frag = new FragType { Drawable = new FragDrawable { Skeleton = new Skeleton { Bones = new SkeletonBonesBlock { Items = new[]{bone} } } },
            PhysicsLODGroup = new FragPhysicsLODGroup { PhysicsLOD1 = new FragPhysicsLOD { Archetype1 = new FragPhysArchetype { Bound = physRoot },
                Children = new ResourcePointerArray64<FragPhysTypeChild> { data_items = new[]{new FragPhysTypeChild { BoneTag = 42, Drawable1 = new FragDrawable { FragMatrix = new Matrix4F_s(Matrix.Translation(.5f,0,0)) } }} } } } };
        frag.Drawable.OwnerFragment = frag; frag.Drawable.Bound = localBound; // alias must not produce two collisions
        var parts = ZonePropCollisions.GetParts(frag.Drawable);
        Check(parts.Count == 1 && (Vector3.TransformCoordinate(Vector3.Zero,parts[0].Transform)-new Vector3(1.5f,2,3)).Length()<.001f, "Fragment bone transform or alias deduplication failed.");
        var def = new CEntityDef { archetypeName = 123, position = new Vector3(2,1,0), rotation = new Vector4(0,0,0,1), scaleXY = 2, scaleZ = 2 };
        var asset = new ZonePackageExporter.Asset { Entity = new YmapEntityDef(new YmapFile(),0,ref def), FragmentDrawable = frag.Drawable };
        var prop = Export(parent,"fragment_prop",null,20,asset);
        Check(prop.PropCollisionParts == 1 && prop.CollisionPrimitives == 1, "Fragment prop collision missing.");
        CompareSupport(ZoneCollisionGeometry.FromBound(localBound),parts[0].Transform * Matrix.Scaling(2) * Matrix.Translation(def.position),((BoundComposite)Reload(prop,false).Bounds).Children.data_items.Single());
        asset.Entity._CEntityDef.flags |= 4;
        var disabled = Export(parent,"disabled_prop",null,20,asset);
        Check(disabled.PropCollisionParts == 0 && disabled.CollisionPrimitives == 0, "Disabled embedded collisions were added.");

        string yft = Path.ChangeExtension(Path.ChangeExtension(fixture,null),"yft.xml");
        var real = XmlYft.GetYft(File.ReadAllText(yft),Path.Combine(Path.GetDirectoryName(fixture),"sollumz_cube"));
        var realParts = ZonePropCollisions.GetParts(real.Fragment.Drawable);
        Check(realParts.Count == 2, "Real Sollumz fragment fixture lost pristine collision parts.");
        var realAsset = new ZonePackageExporter.Asset { Entity = new YmapEntityDef(new YmapFile(),0,ref def), FragmentDrawable = real.Fragment.Drawable };
        var realExport = Export(parent,"real_fragment",null,20,realAsset);
        Check(realExport.CollisionPrimitives == 2 && realExport.PropCollisionParts == 2, "Real fragment failed to export its physical parts.");
        var sourceChildren = ((BoundComposite)real.Fragment.PhysicsLODGroup.PhysicsLOD1.Archetype1.Bound).Children.data_items;
        for (int i = 0; i < realParts.Count; i++)
            Check((realParts[i].Transform.TranslationVector - sourceChildren[i].Transform.TranslationVector).Length() < .001f, "Fixture bone pose and composite placement disagree.");
        Console.WriteLine("PASS: native/cut primitives, closed cuts, concave zone, transforms, materials and fragment collision extraction.");
    }

    static void ExportLayers(string parent)
    {
        var request = new ZonePackageExporter.Request { Name = "collision_layers_" + Guid.NewGuid().ToString("N").Substring(0,8), OutputDirectory = parent,
            Polygon = Square, MinZ = -1, MaxZ = 1 };
        foreach (var prefix in new[] { "", "hi@", "ma@" })
            request.Collisions.Add(new YbnFile { Name = prefix + "source.ybn", Bounds = Primitive(BoundsType.Box) });
        var result = new ZonePackageExporter(request, CancellationToken.None).Export();
        Check(result.CollisionPrimitives == 3 && result.CollisionTriangles == 0, "Separating layers changed totals.");
        var json = new System.Web.Script.Serialization.JavaScriptSerializer();
        var manifest = json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(result.Directory,"manifest.json")));
        var layers = ((System.Collections.IEnumerable)manifest["collisions"]).Cast<Dictionary<string,object>>().ToArray();
        Check((int)manifest["version"] == 3 && layers.Length == 3, "Layer manifest incomplete.");
        foreach (var layer in layers)
        {
            var name = (string)layer["file"]; var file = new YbnFile(); file.Load(File.ReadAllBytes(Path.Combine(result.Directory,name)));
            var xml = XmlYbn.GetYbn(File.ReadAllText(Path.Combine(result.Directory,(string)layer["xml"])));
            Check(((BoundComposite)file.Bounds).Children.data_items.Length == 1 && ((BoundComposite)xml.Bounds).Children.data_items.Length == 1, "Layer lost or duplicated a primitive.");
            Check(name == ((string)layer["role"] == "high" ? "hi@" : (string)layer["role"] == "material" ? "ma@" : "") + request.Name + ".ybn", "Incorrect layer filename.");
        }
        request.Name = "collision_hi_only_" + Guid.NewGuid().ToString("N").Substring(0,8);
        request.Collisions = request.Collisions.Where(b=>b.Name.StartsWith("hi@")).ToList();
        result = new ZonePackageExporter(request,CancellationToken.None).Export();
        manifest = json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(result.Directory,"manifest.json")));
        Check(manifest["collision"] == null && Directory.GetFiles(result.Directory,"*.ybn").Length == 1, "HI-only export fabricated a standard layer or lost geometry.");
    }

    static void ExportWorldCoordinatePrimitives(string parent)
    {
        var pivot = new Vector3(-430.01f, -1500.01f, 25.1f);
        var request = new ZonePackageExporter.Request { Name = "world_primitives_" + Guid.NewGuid().ToString("N").Substring(0,8), OutputDirectory = parent,
            Pivot = pivot, Polygon = Square.Select(p => p * 10 + new Vector2(pivot.X,pivot.Y)).ToArray(), MinZ = pivot.Z - 10, MaxZ = pivot.Z + 10 };
        foreach (var type in new[] { BoundPolygonType.Box, BoundPolygonType.Sphere, BoundPolygonType.Capsule, BoundPolygonType.Cylinder })
        {
            var g = Poly(type); g.CenterGeom = pivot;
            request.Collisions.Add(new YbnFile { Name = type + ".ybn", Bounds = g });
        }
        var result = new ZonePackageExporter(request, CancellationToken.None).Export();
        Check(result.CollisionPrimitives == 4 && result.CollisionTriangles == 0, "World-space primitives changed representation.");
        foreach (bool xml in new[] {false,true})
        {
            var children = ((BoundComposite)Reload(result,xml).Bounds).Children.data_items;
            for (int i = 0; i < request.Collisions.Count; i++)
            {
                var source = (BoundGeometry)request.Collisions[i].Bounds;
                CompareSupport(ZoneCollisionGeometry.FromPolygon(source,source.Polygons.Single()),Matrix.Translation(-pivot),children[i]);
            }
        }
    }
}
