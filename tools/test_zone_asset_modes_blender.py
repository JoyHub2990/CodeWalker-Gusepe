"""Check asset/hierarchy choices and collision display in Blender.

Usage: blender -b --factory-startup --python this.py -- ADDONS PACKAGE HD_XML_LIBRARY [--ragequit-patch]
"""
import ast
import copy
import importlib
import importlib.util
import json
import sys
import tempfile
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

args = sys.argv[sys.argv.index("--") + 1:]
addons, package, library = map(Path, args[:3])
sys.path.insert(0, str(addons))
bpy.ops.preferences.addon_enable(module="Sollumz")
print("Asset import test: Sollumz enabled", flush=True)
if "--ragequit-patch" in args:
    importlib.import_module("ragequit-blender.addon_patcher.sollumz_convert_object_to_asset_patch").register()
spec = importlib.util.spec_from_file_location("zone_import", Path(__file__).with_name("import_zone_package.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
module.register()

# Keep standalone fallback in sync with the real Auto Check operator.
tree = ast.parse((addons / "ragequit-blender/api/operators.py").read_text(encoding="utf-8"))
operator = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "RAGE_OT_api_entities_auto_check")
keywords = next(ast.literal_eval(node.value) for node in operator.body if isinstance(node, ast.Assign)
                and any(isinstance(target, ast.Name) and target.id == "ASSET_KEYWORD" for target in node.targets))
assert tuple(keywords) == module.PROP_KEYWORDS
assert all(module.is_prop_name("PREFIX_" + keyword.upper() + "fixture") for keyword in keywords)
assert not module.is_prop_name("hw1_23_build1")

cube = addons / "Sollumz/tests/assets/sollumz_cube.ydr.xml"
with tempfile.TemporaryDirectory(prefix="zone_asset_policy_", dir=library.parent) as temp:
    temp = Path(temp)
    simple = ET.parse(cube).getroot()
    multi = copy.deepcopy(simple)
    models = multi.find("DrawableModelsHigh")
    models.append(copy.deepcopy(models[0]))
    levels = copy.deepcopy(simple)
    medium = copy.deepcopy(levels.find("DrawableModelsHigh"))
    medium.tag = "DrawableModelsMedium"
    levels.append(medium)
    for name, root, expected in (("simple", simple, True), ("multiple", multi, False), ("lod_levels", levels, True)):
        source = temp / (name + ".ydr.xml")
        ET.ElementTree(root).write(source, encoding="utf-8", xml_declaration=True)
        assert module.asset_import_policy(source, name)[0] == expected, name
        assert module.asset_import_policy(source, "prop_" + name)[0], name
        assert module.asset_import_policy(source, name, "lod")[0], name
    for source, name, reason in ((library / "prop_alarm_01.ydr.xml", "building_collision", "collisions"),
                                 (library / "hw1_23_build1.ydr.xml", "building_lights", "lights")):
        as_asset, actual_reason = module.asset_import_policy(source, name)
        assert not as_asset and reason in actual_reason, (name, actual_reason)
        assert module.asset_import_policy(source, "prop_test")[0]
    # Import a real two-model fixture through the package path and retain its root.
    fixture_manifest = {
        "schema": "ragequit.zone-package", "version": 3, "name": "mode_fixture", "pivot_world": [0, 0, 0],
        "assets": [], "collisions": [], "occlusion": "empty.ymap",
        "references": [{"archetype": "multiple", "asset_name": "multiple", "asset_type": "drawable",
                        "position": [2, 3, 4], "rotation_xyzw": [0, 0, 0, 1], "scale": [1, 1, 1]}],
    }
    # Reuse the package's valid YMAP structure rather than an incomplete XML stub.
    source_manifest = json.loads((package / "manifest.json").read_text(encoding="utf-8-sig"))
    (temp / "empty.ymap.xml").write_bytes((package / (source_manifest["occlusion"] + ".xml")).read_bytes())
    (temp / "manifest.json").write_text(json.dumps(fixture_manifest))
    print("Asset import test: multiple-model fixture", flush=True)
    fixture_instances = module.import_package(temp, temp)
    fixture_base = next(obj.instance_collection for obj in fixture_instances if obj.get("rq_zone_role") == "base")
    fixture_root = next(obj for obj in fixture_base.all_objects if obj.get("rq_zone_archetype"))
    assert fixture_root.type != "MESH" and fixture_root["rq_zone_import_mode"] == "hierarchy"
    assert fixture_root.children and "multiple_models" in fixture_root["rq_zone_import_reason"]

before = set(bpy.data.objects)
manifest = json.loads((package / "manifest.json").read_text(encoding="utf-8-sig"))
print("Asset import test: real package", flush=True)
instances = module.import_package(package, library)
assert len(instances) == 3
assert all(obj.parent is None and tuple(obj.users_collection) == (bpy.context.scene.collection,) for obj in instances)
assert not any(obj.get("rq_zone_control") for obj in set(bpy.data.objects) - before)
base = next(obj.instance_collection for obj in instances if obj["rq_zone_role"] == "base")
roots = [obj for obj in base.all_objects if obj.get("rq_zone_archetype")]
assert len(roots) == len(manifest["references"])
bpy.context.view_layer.update()
unmatched = list(roots)
for ref in manifest["references"]:
    x, y, z, w = ref["rotation_xyzw"]
    expected = Matrix.LocRotScale(Vector(ref["position"]), Quaternion((w, x, y, z)), Vector(ref["scale"]))
    matches = [obj for obj in unmatched if obj["rq_zone_archetype"] == ref["archetype"]
               and max(abs(obj.matrix_world[r][c] - expected[r][c]) for r in range(4) for c in range(4)) < .001]
    assert matches, f"Placement changed: {ref['archetype']} {ref['position']}"
    obj = matches[0]
    unmatched.remove(obj)
    source = library / obj["rq_zone_source"]
    as_asset, reason = module.asset_import_policy(source, ref["archetype"])
    assert obj["rq_zone_import_mode"] == ("asset" if as_asset else "hierarchy")
    if as_asset:
        assert obj.type == "MESH" and not obj.children, f"Asset still has hierarchy: {obj.name}"
        assert obj.data.vertices, f"Empty asset: {obj.name}"
    else:
        assert obj.type != "MESH" and obj.children, f"Hierarchy lost: {obj.name}"
        if "lights" in reason:
            assert any(child.type == "LIGHT" for child in obj.children_recursive), "Building lights lost"
collision = next(obj.instance_collection for obj in instances if obj["rq_zone_role"] == "collision")
assert not collision.children, "Unexpected collision/occlusion subcollection"
assert set(collision.objects) == set(collision.all_objects)
assert any(obj.get("rq_zone_role") == "occlusion" for obj in collision.objects)
assert {obj.get("rq_zone_collision_layer") for obj in collision.objects if obj.get("rq_zone_role") == "collision"} == {"standard", "high", "material"}
collision_meshes = [obj for obj in collision.all_objects if obj.type == "MESH" and obj.get("rq_zone_role") == "collision"]
assert collision_meshes and all(obj.display_type == "TEXTURED" and not obj.show_wire and not obj.show_all_edges for obj in collision_meshes)
lod = next(obj.instance_collection for obj in instances if obj["rq_zone_role"] == "lod")
assert not lod.children, "Unexpected LOD subcollection"
assert {obj["rq_zone_lod_level"] for obj in lod.objects} == {asset["role"] for asset in manifest["assets"]}
assert len(lod.all_objects) == len(manifest["assets"])
assert all(obj.type == "MESH" and not obj.children and obj["rq_zone_import_mode"] == "asset" for obj in lod.all_objects)

# The native Blender command should realize only LOD meshes, without intermediate empties.
native_instance = bpy.data.objects.new("native_lod_realization_test", None)
native_instance.instance_type = "COLLECTION"
native_instance.instance_collection = lod
scene = bpy.context.scene
scene.collection.objects.link(native_instance)
native_instance.location = (12, 34, 7)
for obj in bpy.context.selected_objects:
    obj.select_set(False)
native_instance.select_set(True)
bpy.context.view_layer.objects.active = native_instance
bpy.context.view_layer.update()
before_native = set(bpy.data.objects)
assert "FINISHED" in bpy.ops.object.duplicates_make_real(use_base_parent=True, use_hierarchy=True)
native_created = set(bpy.data.objects) - before_native
assert len(native_created) == len(lod.objects) and all(obj.type == "MESH" for obj in native_created), "Make Instances Real created extra LOD empties"
for obj in native_created | {native_instance}:
    bpy.data.objects.remove(obj, do_unlink=True)

# Realize only the selected root instance; other instances must stay intact.
base_instance = next(obj for obj in instances if obj["rq_zone_role"] == "base")
base_instance.matrix_world = Matrix.Translation((12, 34, 7)) @ Matrix.Rotation(.7, 4, "Z")
bpy.context.view_layer.update()
source_matrices = {obj: obj.matrix_world.copy() for obj in base.all_objects}
expected = [(obj.type, obj.data, base_instance.matrix_world @ obj.matrix_world) for obj in base.all_objects]
bpy.context.view_layer.objects.active = base_instance
base_instance.select_set(True)
before_realization = set(bpy.data.objects)
other_instances = [obj for obj in instances if obj != base_instance]
assert "FINISHED" in bpy.ops.ragequit.make_zone_editable()
bpy.context.view_layer.update()
assert all(obj in set(bpy.data.objects) and obj.instance_type == "COLLECTION" for obj in other_instances)
created = set(bpy.data.objects) - before_realization
assert all(obj.parent is None or obj.parent in created for obj in created), "Realization added a wrapper parent"
copies = [obj for obj in created if obj.get("rq_zone_role") == "base"]
assert len(copies) == len(expected)
for obj_type, data, matrix in expected:
    matches = [obj for obj in copies if obj.type == obj_type and obj.data == data
               and max(abs(obj.matrix_world[r][c] - matrix[r][c]) for r in range(4) for c in range(4)) < .001]
    assert matches, "Realization changed a mesh/light transform"
    copies.remove(matches[0])
assert all(obj.matrix_world == matrix for obj, matrix in source_matrices.items())
assert before.issubset(set(bpy.data.objects))
print(f"PASS: real package {dict(Counter(obj['rq_zone_import_mode'] for obj in roots))}, "
      f"{len(lod.all_objects)} flat LODs, {len(collision_meshes)} shaded collision meshes; "
      "prop rules, multiple-model hierarchy, light hierarchy, placements and rotated realization. "
      f"RageQuit patch: {'--ragequit-patch' in args}", flush=True)
