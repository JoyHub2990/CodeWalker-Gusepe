"""Render zone previews and verify geometry/state preservation and blend persistence.

Usage: blender -b --factory-startup --python this.py -- PACKAGE ADDONS HD_XML OUTPUT
"""
import importlib.util
import sys
from pathlib import Path

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
package, addons, originals, output = map(Path, args[:4])
output.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(addons))
bpy.ops.preferences.addon_enable(module="Sollumz")
spec = importlib.util.spec_from_file_location("zone_import", Path(__file__).with_name("import_zone_package.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
module.tempfile.tempdir = str(output)
module.register()
instances = module.import_package(package, originals)
scene = bpy.context.scene
before_objects = set(bpy.data.objects)
before_scenes = set(bpy.data.scenes)
before_cameras = set(bpy.data.cameras)
before_collections = set(bpy.data.collections)
before_meshes = set(bpy.data.meshes)
before_state = {obj: (obj.matrix_world.copy(), obj.hide_render, obj.hide_viewport, obj.hide_get(), obj.parent, tuple(obj.users_collection))
                for obj in before_objects}
selection = tuple(bpy.context.selected_objects)
active = bpy.context.active_object
active_layer = bpy.context.view_layer.active_layer_collection
for instance in instances:
    module.generate_zone_asset_preview(instance)
    for target in (instance, instance.instance_collection):
        preview = target.preview
        assert tuple(preview.image_size) == (256, 256), f"Missing preview: {target.name}"
        assert tuple(preview.icon_size) == (32, 32)
        assert preview.is_image_custom and preview.is_icon_custom
        pixels = tuple(preview.image_pixels_float)
        alpha = pixels[3::4]
        assert sum(a > .1 for a in alpha) > 100, f"Preview has no geometry: {target.name}"
        assert any(a < .01 for a in alpha), "Preview has no transparent margin"
    # Reproduce the user's workflow: mark the EMPTY instance after importing.
    with bpy.context.temp_override(id=instance, selected_ids=[instance]):
        assert "FINISHED" in bpy.ops.asset.mark()
    assert tuple(instance.preview.image_size) == (256, 256) and instance.preview.is_image_custom
    image = bpy.data.images.new("preview_test_image", width=256, height=256, alpha=True)
    image.pixels[:] = instance.preview.image_pixels_float[:]
    image.filepath_raw = str(output / (instance["rq_zone_role"] + ".png"))
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
assert before_objects == set(bpy.data.objects)
assert before_scenes == set(bpy.data.scenes)
assert before_cameras == set(bpy.data.cameras)
assert before_collections == set(bpy.data.collections)
assert before_meshes == set(bpy.data.meshes)
assert bpy.context.scene == scene and bpy.context.active_object == active
assert tuple(bpy.context.selected_objects) == selection
assert bpy.context.view_layer.active_layer_collection == active_layer
for obj, values in before_state.items():
    assert values == (obj.matrix_world, obj.hide_render, obj.hide_viewport, obj.hide_get(), obj.parent, tuple(obj.users_collection)), f"Preview changed source state: {obj.name}"
assert "FINISHED" in bpy.ops.ragequit.generate_zone_asset_preview()
print("PASS: three nonempty previews, instance Mark as Asset, hidden sources/collisions, regeneration, scene and geometry preservation", flush=True)
saved = [(instance.name, instance.instance_collection.name, tuple(instance.preview.image_pixels)) for instance in instances]
bpy.ops.wm.save_as_mainfile(filepath=str(output / "previews.blend"), check_existing=False)
bpy.ops.wm.open_mainfile(filepath=str(output / "previews.blend"))
for name, source_name, pixels in saved:
    instance = bpy.data.objects[name]
    assert instance.asset_data and instance.preview.is_image_custom
    assert tuple(instance.preview.image_pixels) == pixels
    assert tuple(bpy.data.collections[source_name].preview.image_pixels) == pixels
print("PASS: instance and collection thumbnails preserved after saving and reopening blend", flush=True)
