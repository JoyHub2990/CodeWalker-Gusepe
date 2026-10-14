"""Headless integration check. Usage: blender -b --factory-startup --python this.py -- PACKAGE_DIR ADDON_DIR"""
import importlib.util
import json
import sys
import tempfile
from pathlib import Path
import bpy
from mathutils import Matrix, Quaternion, Vector

args = sys.argv[sys.argv.index("--") + 1:]
package = Path(args[0])
sys.path.insert(0, args[1])
bpy.ops.preferences.addon_enable(module="Sollumz")
spec = importlib.util.spec_from_file_location("zone_import", Path(__file__).with_name("import_zone_package.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
module.register()
before = set(bpy.data.objects)
expected_manifest = (package / "manifest.json").resolve()
assert module.resolve_manifest_path(package) == expected_manifest
assert module.resolve_manifest_path(str(package) + "/") == expected_manifest
assert module.resolve_manifest_path(expected_manifest) == expected_manifest
with tempfile.TemporaryDirectory(prefix="zone_import_path_", dir=package.parent) as empty:
    for invalid in ("", empty, Path(empty) / "missing.json"):
        try:
            module.import_package(invalid)
            raise AssertionError(f"Invalid manifest path accepted: {invalid}")
        except ValueError as exc:
            assert "Select" in str(exc) and "manifest.json" in str(exc), str(exc)
    assert set(bpy.data.objects) == before, "Path validation changed the scene"
manifest = json.loads((package / "manifest.json").read_text(encoding="utf-8-sig"))
references = manifest.get("references", [])
originals = Path(args[2]) if len(args) > 2 else Path(args[1]) / "Sollumz" / "tests" / "assets"
if references:
    try:
        module.import_package(package / "manifest.json", use_api=False)
        raise AssertionError("Missing HD library was silently accepted")
    except ValueError as exc:
        assert "Original HD XML folder" in str(exc), str(exc)
    assert set(bpy.data.objects) == before, "Preflight failure changed the scene"
before_collections = set(bpy.data.collections)
instances = module.import_package(package, originals)
assert len(instances) == 3, "Expected three instances"
assert all(o.parent is None and tuple(o.users_collection) == (bpy.context.scene.collection,) for o in instances), "Instances must be directly at the scene root"
assert not any(o.get("rq_zone_control") for o in set(bpy.data.objects) - before)
assert len(set(bpy.data.collections) - before_collections) == 4, "Unexpected placement or technical subcollection"
assert {o["rq_zone_role"] for o in instances} == {"base", "collision", "lod"}
assert all(o.instance_type == "COLLECTION" for o in instances)
assert all(o.instance_collection.asset_data for o in instances)
assert all(o.location.length < 1e-6 for o in instances)
base = next(o.instance_collection for o in instances if o["rq_zone_role"] == "base")
meshes = [o for o in base.all_objects if o.type == "MESH"]
assert meshes, "No base meshes"
bpy.context.view_layer.update()
if references:
    roots = [o for o in base.all_objects if o.get("rq_zone_archetype")]
    assert len(roots) == len(references), "Missing HD instances"
    for root, ref in zip(roots, references):
        x, y, z, w = ref["rotation_xyzw"]
        expected_matrix = Matrix.LocRotScale(Vector(ref["position"]), Quaternion((w, x, y, z)), Vector(ref["scale"]))
        assert max(abs(root.matrix_world[r][c] - expected_matrix[r][c]) for r in range(4) for c in range(4)) < .001, "Original HD transform incorrect"
    assert any(abs((o.matrix_world @ v.co).z) > .501 for o in meshes for v in o.data.vertices), "Original HD was clipped"
else:
    for obj in meshes:
        for vertex in obj.data.vertices:
            p = obj.matrix_world @ vertex.co
            assert abs(p.x) <= 1.001 and abs(p.y) <= 1.001 and abs(p.z) <= .501, tuple(p)
assert any(o.get("rq_zone_role") == "occlusion" for o in bpy.data.objects), "No occluders imported"
source_positions = {o: o.matrix_world.copy() for o in base.all_objects}
transform = Matrix.Translation((12,34,7)) @ Matrix.Rotation(0.7,4,"Z")
for instance in instances:
    instance.matrix_world = transform
    instance.hide_set(False)
    instance.select_set(True)
bpy.context.view_layer.update()
expected = {o.data: transform @ o.matrix_world for o in meshes}
bpy.context.view_layer.objects.active = instances[0]
before_realization = set(bpy.data.objects)
instance_names = [o.name for o in instances]
assert "FINISHED" in bpy.ops.ragequit.make_zone_editable()
bpy.context.view_layer.update()
assert all(name not in bpy.data.objects for name in instance_names), "Instances not realized"
copies = [o for o in set(bpy.data.objects) - before_realization if o.type == "MESH" and o.get("rq_zone_role") == "base"]
assert len(copies) == len(meshes)
assert all(max(abs(o.matrix_world[r][c] - expected[o.data][r][c]) for r in range(4) for c in range(4)) < 0.001 for o in copies), "Placement changed on realization"
assert all(o.matrix_world == matrix for o,matrix in source_positions.items()), "Source hierarchy changed"
assert all(any(o.data == original.data for original in meshes) for o in copies), "Mesh sharing lost"
assert before.issubset(set(bpy.data.objects)), "Existing scene modified"
print("PASS: package folder/manifest paths, invalid path preflight, original HD placement, three root instances without wrapper, occlusion, shared meshes, rotated multi-selection realization, source preservation")
