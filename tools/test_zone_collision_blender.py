"""Headless Sollumz check: -- PACKAGE_TEST_DIRECTORY ADDONS_DIRECTORY."""
import sys
import importlib.util
from pathlib import Path
import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
packages = Path(args[0])
sys.path.insert(0, args[1])
bpy.ops.preferences.addon_enable(module="Sollumz")
cases = ["native_" + kind for kind in ("box", "sphere", "capsule", "cylinder", "disc")]
cases += ["native_poly_" + kind for kind in ("box", "sphere", "capsule", "cylinder")]
cases += ["cut_" + kind for kind in ("box", "sphere", "capsule", "cylinder", "disc")]
cases += ["cut_poly_" + kind for kind in ("box", "sphere", "capsule", "cylinder")]
cases = [(case, suffix) for case in cases for suffix in (".ybn.xml", ".ybn")]
for case, suffix in cases:
    matches = [p for p in packages.glob(case + "_*") if ".partial-" not in p.name]
    folder = max(matches, key=lambda p: p.stat().st_mtime_ns)
    source = next(folder.glob("*" + suffix))
    before = set(bpy.data.objects)
    result = bpy.ops.sollumz.import_assets(directory=str(folder), files=[{"name": source.name}], use_custom_settings=True, import_as_asset=False)
    assert "FINISHED" in result, case
    created = set(bpy.data.objects) - before
    meshes = [o for o in created if o.type == "MESH"]
    assert meshes, f"No collision geometry imported: {case}"
    bpy.context.view_layer.update()
    points = [obj.matrix_world @ v.co for obj in meshes for v in obj.data.vertices]
    if case.startswith("cut_"):
        assert all(max(abs(p.x), abs(p.y), abs(p.z)) < 1.003 for p in points), f"Cut exceeds zone: {case}"
        assert all(getattr(obj, "sollum_type", "") != "sollumz_bound_poly_box" for obj in meshes), case
    else:
        center = sum(points, Vector()) / len(points)
        assert (center - Vector((2, 3, 1))).length < .6, f"Native placement incorrect: {case}: {center}"
        suffix = case.split("_")[-1]
        expected = "sollumz_bound_" + ("poly_" if "_poly_" in case else "") + suffix
        assert any(getattr(obj, "sollum_type", "") == expected for obj in created), f"Native type not retained: {case}"
    for obj in created:
        bpy.data.objects.remove(obj, do_unlink=True)
print(f"PASS: {len(cases)} collision imports, native types/placements and clipped geometry.")

spec = importlib.util.spec_from_file_location("zone_import", Path(__file__).with_name("import_zone_package.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
for case, roles in [("collision_layers", {"standard", "high", "material"}), ("collision_hi_only", {"high"})]:
    folder = max((p for p in packages.glob(case + "_*") if ".partial-" not in p.name), key=lambda p: p.stat().st_mtime_ns)
    instances = module.import_package(folder / "manifest.json")
    assert len(instances) == 3, "Layer import changed the three-instance workflow"
    collision = next(o.instance_collection for o in instances if o.get("rq_zone_role") == "collision")
    assert not collision.children, "Collision subcollections were recreated"
    assert {o.get("rq_zone_collision_layer") for o in collision.objects if o.get("rq_zone_collision_layer")} == roles
    assert sum(o.type == "MESH" for o in collision.all_objects) == len(roles), "Layer missing or imported twice"
print("PASS: layered and HI-only packages imported into the shared collision instance.")
