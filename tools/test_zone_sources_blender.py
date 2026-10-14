"""Check editable collection sources, live instances, old imports and render isolation.

Usage: blender -b --factory-startup --python this.py -- PACKAGE ADDONS OUTPUT_DIR
"""
import importlib.util
import sys
from pathlib import Path

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
package, addons, output = map(Path, args[:3])
output.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(addons))
bpy.ops.preferences.addon_enable(module="Sollumz")
spec = importlib.util.spec_from_file_location("zone_import", Path(__file__).with_name("import_zone_package.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
module.register()
scene = bpy.context.scene
instances = module.import_package(package, addons / "Sollumz/tests/assets")
base_instance = next(obj for obj in instances if obj.get("rq_zone_role") == "base")
base = base_instance.instance_collection
group = next(coll for coll in scene.collection.children if coll.get("rq_zone_source_group"))
assert set(group.children) == {obj.instance_collection for obj in instances}
assert base.name.endswith("_assets")
assert group.hide_render and not base.hide_render and not base.hide_viewport
layer = module.find_layer_collection(bpy.context.view_layer.layer_collection, group)
assert layer.hide_viewport and not layer.exclude
mesh = next(obj for obj in base.all_objects if obj.type == "MESH")
duplicate = base_instance.copy()
scene.collection.objects.link(duplicate)
duplicate.location.x = 10


def occurrences():
    bpy.context.view_layer.update()
    graph = bpy.context.evaluated_depsgraph_get()
    return [(item.is_instance, item.object.data.vertices[0].co.copy()) for item in graph.object_instances
            if item.object.original == mesh and item.show_self
            and (item.parent.original.visible_get() if item.is_instance else item.object.original.visible_get())]


items = occurrences()
assert not mesh.visible_get(), "Source mesh remains directly visible while its folder is hidden"
assert len(items) == 2 and all(is_instance for is_instance, _ in items), f"Hidden sources suppressed or duplicated instances: {items}"
bpy.context.view_layer.objects.active = base_instance
assert "FINISHED" in bpy.ops.ragequit.show_zone_sources()
assert not layer.hide_viewport
items = occurrences()
assert len(items) == 3 and sum(not instance for instance, _ in items) == 1
point = mesh.data.vertices[0]
original_point = point.co.copy()
point.co += Vector((.125, .25, .375))
mesh.data.update()
assert all((co - point.co).length < 1e-5 for _, co in occurrences()), "Source edits did not reach all instances"
point.co = original_point
mesh.data.update()
layer.hide_viewport = True
assert len(occurrences()) == 2

# Recover an old import whose definitions have no scene links, without copying data.
control = bpy.data.objects.new("legacy_zone_control", None)
control["rq_zone_control"] = True
scene.collection.objects.link(control)
for instance in instances:
    instance.parent = control
for source in list(group.children):
    group.children.unlink(source)
bpy.data.collections.remove(group)
before_objects = set(bpy.data.objects)
before_meshes = set(bpy.data.meshes)
bpy.context.view_layer.objects.active = control
assert "FINISHED" in bpy.ops.ragequit.show_zone_sources()
group = next(coll for coll in scene.collection.children if coll.get("rq_zone_source_group"))
assert set(group.children) == {obj.instance_collection for obj in instances}
assert set(bpy.data.objects) == before_objects and set(bpy.data.meshes) == before_meshes
collision_instance = next(obj for obj in instances if obj.get("rq_zone_role") == "collision")
collision_layer = module.find_layer_collection(bpy.context.view_layer.layer_collection, collision_instance.instance_collection)
assert collision_layer.hide_viewport, "Recovered technical sources should initially stay hidden"
bpy.context.view_layer.objects.active = collision_instance
assert "FINISHED" in bpy.ops.ragequit.show_zone_sources()
assert not collision_layer.hide_viewport, "Cannot show selected collision source for editing"
before_collections = set(bpy.data.collections)
assert "FINISHED" in bpy.ops.ragequit.show_zone_sources()
assert set(bpy.data.collections) == before_collections, "Repeated source command duplicated collections"
# Asset Browser drops have no package control parent and must work too.
duplicate.parent = None
bpy.context.view_layer.objects.active = duplicate
assert "FINISHED" in bpy.ops.ragequit.show_zone_sources()
assert duplicate.instance_collection == base

# Importing another package with a hidden source selected must still permit asset joins.
module.find_layer_collection(bpy.context.view_layer.layer_collection, group).hide_viewport = True
second = module.import_package(package, addons / "Sollumz/tests/assets")
assert len(second) == 3
assert any(obj.type == "MESH" for instance in second if instance.get("rq_zone_role") == "base"
           for obj in instance.instance_collection.all_objects)
print("PASS: linked source collections, hidden source occurrences, live mesh edits across two instances, recovery, idempotence, Asset Browser instances, import from hidden source context", flush=True)

# Compare actual rendered pixels with unlinked and linked definitions. Sources
# must not render at the origin and their instances must remain renderable.
render_scene = bpy.data.scenes.new("source_render_test")
render_scene.render.engine = "CYCLES"
render_scene.cycles.device = "CPU"
render_scene.cycles.samples = 1
render_scene.cycles.use_denoising = False
render_scene.render.resolution_x = 32
render_scene.render.resolution_y = 32
render_scene.render.resolution_percentage = 100
render_scene.render.image_settings.file_format = "PNG"
camera = bpy.data.objects["Camera"].copy()
render_scene.collection.objects.link(camera)
render_scene.camera = camera
definition = bpy.data.collections.new("render_source")
cube = bpy.data.objects["Cube"].copy()
cube.data = cube.data.copy()
definition.objects.link(cube)
material = bpy.data.materials.new("source_render_material")
material.use_nodes = True
material.node_tree.nodes.clear()
emission = material.node_tree.nodes.new("ShaderNodeEmission")
emission.inputs["Color"].default_value = (.8, .1, .1, 1)
surface = material.node_tree.nodes.new("ShaderNodeOutputMaterial")
material.node_tree.links.new(emission.outputs[0], surface.inputs["Surface"])
cube.data.materials.clear()
cube.data.materials.append(material)
instance = bpy.data.objects.new("render_instance", None)
instance.instance_type = "COLLECTION"
instance.instance_collection = definition
instance.location.x = 2
render_scene.collection.objects.link(instance)


def pixels(label):
    path = output / (label + ".png")
    render_scene.render.filepath = str(path)
    bpy.ops.render.render(scene=render_scene.name, write_still=True)
    image = bpy.data.images.load(str(path), check_existing=False)
    result = tuple(image.pixels)
    bpy.data.images.remove(image)
    return result


expected = pixels("sources_unlinked")
assert max(expected[0::4]) > .2, "Reference render has no visible mesh"
render_group = module.link_zone_sources(render_scene, [definition], "render_test")
render_layer = module.find_layer_collection(render_scene.view_layers[0].layer_collection, render_group)
render_layer.hide_viewport = False
assert pixels("sources_linked") == expected, "Editable source folder changed the render"
print("PASS: render unchanged with editable sources visible; no duplicate geometry and collection instances still render", flush=True)
