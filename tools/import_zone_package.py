"""Run in Blender's Text Editor with Sollumz enabled, then select manifest.json.

A package directory is also accepted and resolves to its manifest.json.

Generated geometry is already in the package frame. Original HD resources are
resolved through the RageQuit API or a user-selected XML library, then placed
using the manifest. API exports are kept in a separate cache, outside the package.
This script never saves preferences or the current file.
"""
import json
import tempfile
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path
from urllib import request, error
import bpy
from bpy.props import StringProperty, BoolProperty
from bpy_extras.io_utils import ImportHelper
from mathutils import Matrix, Quaternion, Vector


def resolve_manifest_path(manifest_path):
    if not str(manifest_path).strip():
        raise ValueError("Select a package folder or its manifest.json file")
    path = Path(bpy.path.abspath(str(manifest_path))).resolve()
    if path.is_dir():
        path = path / "manifest.json"
    if not path.is_file():
        raise ValueError(f"Package manifest not found: {path}. Select the package folder or its manifest.json file")
    return path


def reference_names(ref):
    return list(dict.fromkeys(n.lower() for n in (ref.get("asset_name"), ref["archetype"]) if n))


# Same naming rules as rage.api_entities_auto_check. Read its live list when
# RageQuit is enabled without changing the user's API entity selection list.
PROP_KEYWORDS = ("prop_", "_prop", "csx_", "cs_x_", "v_res_", "v_club_", "v_corp_",
                 "v_serv_", "v_ret_", "v_ind_", "v_ilev_", "v_med_")


def is_prop_name(name):
    operator = getattr(bpy.types, "RAGE_OT_api_entities_auto_check", None)
    keywords = getattr(operator, "ASSET_KEYWORD", PROP_KEYWORDS)
    return any(keyword in name.lower() for keyword in keywords)


def asset_import_policy(source, name, role="base"):
    """Return (import_as_asset, reason), before embedded bounds are stripped."""
    if role in {"lod", "slod1", "slod2", "slod3", "slod4"}:
        return True, "lod"
    if is_prop_name(name):
        return True, "prop"
    root = ET.parse(source).getroot()
    reasons = []
    if any(bound.get("type", "").lower() not in {"", "none", "null"} or len(bound)
           for bound in root.iter("Bounds")):
        reasons.append("collisions")
    if any(len(lights.findall("Item")) for lights in root.iter("Lights")):
        reasons.append("lights")
    counts = [sum(len(group.findall("Item")) for group in root.iter(tag))
              for tag in ("DrawableModelsHigh", "DrawableModelsMedium", "DrawableModelsLow", "DrawableModelsVeryLow")]
    if max(counts) > 1:
        reasons.append("multiple_models")
    if not any(counts) or root.tag not in {"Drawable", "Fragment"}:
        reasons.append("unsupported_asset_layout")
    return not reasons, ",".join(reasons) if reasons else "simple_drawable"


def resolve_xml_originals(references, library):
    library = Path(bpy.path.abspath(str(library))).resolve()
    if not library.is_dir():
        raise ValueError(f"Original HD XML folder does not exist: {library}")
    index = {}
    for source in library.rglob("*.xml"):
        if source.is_file() and source.name.lower().endswith((".ydr.xml", ".yft.xml")):
            index.setdefault(source.name.lower(), []).append(source)
    originals, missing = [], []
    for ref in references:
        matches = []
        extension = ".yft.xml" if ref.get("asset_type") == "fragment" else ".ydr.xml"
        for asset_name in reference_names(ref):
            matches = index.get(asset_name + extension, [])
            if matches:
                break
        if len(matches) > 1:
            raise ValueError(f"Multiple originals for {ref['archetype']}; choose a folder with one version per model")
        if not matches:
            missing.append(ref["archetype"])
        else:
            originals.append((matches[0], ref))
    if missing:
        names = sorted(set(missing))
        raise ValueError(f"Missing original HD XML models ({len(names)}): " + ", ".join(names[:12]))
    return originals


def api_post(endpoint, route, payload, timeout):
    endpoint = endpoint.strip().rstrip("/")
    if not endpoint.startswith(("http://", "https://")):
        endpoint = "http://" + endpoint
    endpoint = endpoint.replace("localhost:", "127.0.0.1:")
    if not endpoint.endswith("/api"):
        endpoint += "/api"
    req = request.Request(endpoint + route, data=json.dumps(payload).encode("utf-8"),
                          headers={"Content-Type": "application/json"})
    try:
        # Match RageQuit's direct connection: do not send local requests to an OS proxy.
        with request.build_opener(request.ProxyHandler({})).open(req, timeout=timeout) as response:
            return json.load(response)
    except error.HTTPError as exc:
        raise RuntimeError(f"RageQuit API {route}: HTTP {exc.code}. Check the API game configuration.") from exc
    except (error.URLError, TimeoutError, OSError) as exc:
        raise RuntimeError(f"Cannot reach RageQuit API at {endpoint}. Start the API service used by RageQuit, "
                           "or select Original HD XML folder.") from exc


def resolve_api_originals(references, endpoint=None, cache_parent=None):
    scene = bpy.context.scene
    endpoint = endpoint or getattr(scene, "api_endpoint", "127.0.0.1:5555")
    names = sorted({name for ref in references for name in reference_names(ref)})
    settings = {
        "CustomPaths": [bpy.path.abspath(item.path) for item in getattr(scene, "api_custom_paths", []) if item.path.strip()],
        "ExcludeGameFiles": bool(getattr(scene, "api_exclude_game_files", False)),
    }
    print(f"[Zone package] Searching original HD models ({len(names)} names)...", flush=True)
    paths = api_post(endpoint, "/search-files", {
        "Filenames": names, "Filters": [".ydr", ".yft"], "MatchWholeWord": True, **settings,
    }, 60)
    if not isinstance(paths, list) or any(not isinstance(p, str) for p in paths):
        raise ValueError("Unexpected RageQuit search response; expected a list of file paths")
    index = {}
    for source in dict.fromkeys(paths):
        filename = source.replace("\\", "/").rsplit("/", 1)[-1].lower()
        index.setdefault(filename, []).append(source)
    selected, warnings, missing = {}, [], []
    for ref in references:
        extension = ".yft" if ref.get("asset_type") == "fragment" else ".ydr"
        matches = []
        for asset_name in reference_names(ref):
            matches = index.get(asset_name + extension, [])
            if matches:
                break
        if not matches:
            missing.append(ref["archetype"])
            continue
        # Same search-order selection as the existing RageQuit bridge. Report
        # ambiguity explicitly: older manifests do not record the source archive.
        if len(matches) > 1:
            warning = f"Multiple HD sources for {ref['archetype']}; RageQuit search order selected: {matches[0]}"
            if warning not in warnings:
                warnings.append(warning)
                print("[Zone package] " + warning, flush=True)
        selected[matches[0]] = None
    if missing:
        raise ValueError("HD models not found by RageQuit: " + ", ".join(sorted(set(missing))[:12])
                         + ". Check API search paths, or use an Original HD XML folder (YDD entries need individual YDR XMLs).")
    # Keep the cache so external texture paths remain valid after saving/reopening Blender.
    cache_root = Path(cache_parent) if cache_parent else Path(tempfile.gettempdir()) / "ragequit-blender" / "zone-hd"
    cache_root.mkdir(parents=True, exist_ok=True)
    cache = cache_root / uuid.uuid4().hex
    # Inherit normal folder permissions. Windows mkdtemp uses owner-only access;
    # files created by an elevated API service would then be unreadable by Blender.
    cache.mkdir()
    print(f"[Zone package] Extracting {len(selected)} unique HD files with textures to {cache}", flush=True)
    response = api_post(endpoint, "/download-files", {
        "FullPaths": list(selected), "Output": str(cache), "Xml": True, "Textures": True,
        "ExcludeGameFiles": settings["ExcludeGameFiles"],
    }, 300)
    def check_errors(value):
        if isinstance(value, list):
            for item in value:
                check_errors(item)
        elif isinstance(value, dict):
            if value.get("error"):
                raise RuntimeError(f"RageQuit extraction failed: {value['error']}")
            check_errors(value.get("results", []))
    if not isinstance(response, list):
        raise ValueError("Unexpected RageQuit extraction response; expected a list")
    check_errors(response)
    originals = resolve_xml_originals(references, cache)
    (cache / "sources.json").write_text(json.dumps({"sources": list(selected), "warnings": warnings}, indent=2), encoding="utf-8")
    return originals, warnings, cache


def find_layer_collection(layer, collection):
    if layer.collection == collection:
        return layer
    for child in layer.children:
        found = find_layer_collection(child, collection)
        if found is not None:
            return found
    return None


def link_zone_sources(scene, collections, name, hide_sources=True):
    """Link the actual instance definitions, never copies of their objects."""
    collections = list(dict.fromkeys(collections))
    group = next((coll for coll in scene.collection.children if coll.get("rq_zone_source_group")
                  and any(source in tuple(coll.children) for source in collections)), None)
    new_group = group is None
    if new_group:
        group = bpy.data.collections.new(name + "_sources")
        group["rq_zone_source_group"] = True
        scene.collection.children.link(group)
    # Only the enclosing folder is disabled for rendering. Its child asset
    # collections stay renderable through their collection instances.
    group.hide_render = True
    added = []
    for source in collections:
        if source not in tuple(group.children):
            group.children.link(source)
            added.append(source)
        if source in tuple(scene.collection.children):
            scene.collection.children.unlink(source)
    for view_layer in scene.view_layers:
        view_layer.update()
        layer = find_layer_collection(view_layer.layer_collection, group)
        if layer is not None and (new_group or hide_sources):
            # Hide only this scene occurrence, not the underlying collections.
            layer.hide_viewport = True
        for source in added:
            if source.get("rq_zone_role") in {"collision", "lod"}:
                source_layer = find_layer_collection(view_layer.layer_collection, source)
                if source_layer is not None:
                    source_layer.hide_viewport = True
    return group


def selected_zone_instances(obj, selection=()):
    if obj is None:
        return []
    control = obj if obj.get("rq_zone_control") else obj.parent
    # Older imports use a shared control; new imports and Asset Browser drops
    # are independent instances directly in the scene collection.
    candidates = control.children if control and control.get("rq_zone_control") else dict.fromkeys([obj, *selection])
    return [item for item in candidates if item.instance_type == "COLLECTION" and item.instance_collection
            and item.instance_collection.get("rq_zone_role") in {"base", "collision", "lod"}]


def generate_zone_asset_preview(instance):
    """Render the collection geometry: Blender's automatic object preview rejects empties."""
    if not instance.is_editable:
        raise ValueError("Open the asset's source blend file to change its preview")
    source = instance.instance_collection
    if source is None:
        raise ValueError("Select a zone collection instance")
    bpy.context.view_layer.update()
    originals = [obj for obj in source.all_objects if obj.type in {"MESH", "CURVE", "SURFACE", "FONT", "META"}]
    # The LOD collection deliberately contains every level. Show just the best
    # level in its thumbnail, avoiding superimposed surfaces from all four meshes.
    levels = {obj.get("rq_zone_lod_level") for obj in originals} - {None}
    if source.get("rq_zone_role") == "lod" and levels:
        order = {name: i for i, name in enumerate(("lod", "slod1", "slod2", "slod3", "slod4"))}
        level = min(levels, key=lambda name: order.get(name, 99))
        originals = [obj for obj in originals if obj.get("rq_zone_lod_level") == level]
    elif source.get("rq_zone_role") == "base":
        # Respect individual Sollumz model visibility; the source folder's eye
        # and render exclusion must not prevent rendering the asset itself.
        originals = [obj for obj in originals if not obj.hide_get() and not obj.hide_render]
    if not originals:
        raise ValueError(f"No geometry to preview in {source.name}")
    scene = bpy.data.scenes.new("_rq_zone_preview")
    objects, camera_data = [], None
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.render.resolution_x = scene.render.resolution_y = 256
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = "PNG"
        scene.render.image_settings.color_mode = "RGBA"
        scene.render.film_transparent = True
        shading = scene.display.shading
        shading.light = "STUDIO"
        shading.color_type = "MATERIAL"
        shading.show_shadows = True
        shading.show_cavity = True
        shading.cavity_type = "BOTH"
        for original in originals:
            copied = original.copy()
            objects.append(copied)
            copied.parent = None
            copied.matrix_world = original.matrix_world.copy()
            copied.hide_render = False
            copied.hide_viewport = False
            copied.display_type = "TEXTURED"
            scene.collection.objects.link(copied)
        scene.view_layers[0].update()
        points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
        low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
        high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
        center = (low + high) / 2
        diameter = max((high - low).length, .1)
        camera_data = bpy.data.cameras.new("_rq_zone_preview_camera")
        camera = bpy.data.objects.new("_rq_zone_preview_camera", camera_data)
        objects.append(camera)
        scene.collection.objects.link(camera)
        camera.location = center + Vector((1, -1, .75)).normalized() * (diameter * 3 + 1)
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        camera_data.type = "ORTHO"
        scene.view_layers[0].update()
        local_points = [camera.matrix_world.inverted() @ p for p in points]
        camera_data.ortho_scale = max(max(p[i] for p in local_points) - min(p[i] for p in local_points) for i in (0, 1)) * 1.15
        camera_data.ortho_scale = max(camera_data.ortho_scale, .1)
        camera_data.clip_start = .01
        camera_data.clip_end = diameter * 10 + 10
        scene.camera = camera
        with tempfile.TemporaryDirectory(prefix="rq_zone_preview_") as directory:
            path = str(Path(directory) / "preview.png")
            with bpy.context.temp_override(scene=scene, view_layer=scene.view_layers[0]):
                result = bpy.ops.render.render(write_still=False, scene=scene.name)
            if "FINISHED" not in result:
                raise RuntimeError("Preview render did not finish")
            # Save explicitly: render.filepath interprets braces in Windows temp
            # folder names as Blender path-template expressions.
            bpy.data.images["Render Result"].save_render(path, scene=scene)
            image = bpy.data.images.load(path, check_existing=False)
            try:
                size = tuple(image.size)
                pixels = tuple(image.pixels)
                if not any(pixels[3::4]):
                    raise RuntimeError("Preview is empty; use Capture Screenshot Preview in the Asset Browser")
                image.scale(32, 32)
                icon = tuple(image.pixels)
                # Object preview survives a later Mark as Asset. The collection
                # asset also receives it, so both Asset Browser workflows work.
                targets = [instance] + ([source] if source.is_editable else [])
                for target in targets:
                    preview = target.preview_ensure()
                    preview.image_size = size
                    preview.image_pixels_float[:] = pixels
                    preview.is_image_custom = True
                    preview.icon_size = (32, 32)
                    preview.icon_pixels_float[:] = icon
                    preview.is_icon_custom = True
            finally:
                bpy.data.images.remove(image)
    finally:
        for obj in objects:
            bpy.data.objects.remove(obj, do_unlink=True)
        if camera_data is not None:
            bpy.data.cameras.remove(camera_data)
        bpy.data.scenes.remove(scene)


def import_package(manifest_path, originals_directory=None, *, use_api=True, api_endpoint=None, cache_parent=None):
    path = resolve_manifest_path(manifest_path)
    manifest = json.loads(path.read_text(encoding="utf-8-sig"))
    if manifest.get("schema") != "ragequit.zone-package" or manifest.get("version") not in {1, 2, 3}:
        raise ValueError("Unsupported zone package manifest")
    if not hasattr(bpy.ops.sollumz, "import_assets"):
        raise RuntimeError("Enable Sollumz before importing a zone package")
    directory = path.parent
    name = manifest["name"]

    def source_file(filename):
        source = (directory / filename).resolve()
        if source.parent != directory or not source.is_file():
            raise ValueError(f"Missing or invalid package file: {filename}")
        return source

    # Check files before touching the scene. XML works without native szio.
    assets = [(source_file(a["xml"]), a) for a in manifest["assets"]]
    references = manifest.get("references", [])
    originals = []
    import_warnings, hd_cache = [], None
    collision_entries = manifest.get("collisions")
    if collision_entries is None:
        collision_entries = [{"xml": manifest["collision"] + ".xml", "role": "standard"}] if manifest.get("collision") else []
    collisions = [(source_file(entry["xml"]), entry["role"]) for entry in collision_entries]
    if len({source for source, _ in collisions}) != len(collisions):
        raise ValueError("Duplicate collision file in manifest")
    occlusion = source_file(manifest["occlusion"] + ".xml")
    if references:
        if originals_directory:
            originals = resolve_xml_originals(references, originals_directory)
        elif use_api:
            originals, import_warnings, hd_cache = resolve_api_originals(references, api_endpoint, cache_parent)
        else:
            raise ValueError("Select Original HD XML folder or enable Fetch HD via RageQuit API")
    scene = bpy.context.scene
    before_objects = set(bpy.data.objects)
    before_collections = set(bpy.data.collections)
    before_images = set(bpy.data.images)
    previous_active = bpy.context.view_layer.objects.active
    previous_active_layer = bpy.context.view_layer.active_layer_collection
    previous_selection = list(bpy.context.selected_objects)
    definitions = {}
    policies = {}

    def policy(source, name, role):
        key = (source, name, role)
        if key not in policies:
            policies[key] = asset_import_policy(source, name, role)
        return policies[key]

    try:
        # Asset conversion joins objects in the active collection. A source
        # folder edited earlier may now be hidden, so use the visible scene root.
        bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection
        for role in ("base", "collision", "lod"):
            coll = bpy.data.collections.new(f"{name}_{'assets' if role == 'base' else role}")
            coll["rq_zone_role"] = role
            coll["rq_zone_package"] = name
            coll.instance_offset = (0, 0, 0)
            scene.collection.children.link(coll)
            definitions[role] = coll
        def load(source, target, role, as_asset=False, reason=""):
            before = set(bpy.data.objects)
            result = bpy.ops.sollumz.import_assets(
                directory=str(source.parent), files=[{"name": source.name}],
                use_custom_settings=True, import_as_asset=as_asset,
            )
            if "FINISHED" not in result:
                raise RuntimeError(f"Sollumz could not import {source.name}")
            created = set(bpy.data.objects) - before
            for obj in created:
                for collection in list(obj.users_collection):
                    collection.objects.unlink(obj)
                target.objects.link(obj)
                obj["rq_zone_role"] = role
                obj["rq_zone_source"] = source.name
                if role in {"base", "lod"}:
                    obj["rq_zone_import_mode"] = "asset" if as_asset else "hierarchy"
                    obj["rq_zone_import_reason"] = reason
                if role in {"collision", "occlusion"}:
                    obj.hide_render = True
                    obj.display_type = "TEXTURED" if role == "collision" else "WIRE"
                    if role == "collision":
                        obj.show_wire = False
                        obj.show_all_edges = False
            return created

        for source, asset in assets:
            role = asset["role"]
            target = definitions["base" if role == "base" else "lod"]
            as_asset, reason = policy(source, source.name.removesuffix(".ydr.xml"), role)
            created = load(source, target, "base" if role == "base" else "lod", as_asset, reason)
            if not created:
                raise RuntimeError(f"No objects imported from {source.name}")
            if role != "base":
                for obj in created:
                    obj["rq_zone_lod_level"] = role
        for source, ref in originals:
            as_asset, reason = policy(source, ref["archetype"], "base")
            created = load(source, definitions["base"], "base", as_asset, reason)
            if not created:
                raise RuntimeError(f"No objects imported from original {source.name}")
            # Collision comes from the clipped package YBN. Do not duplicate uncut
            # embedded bounds from external HD models in the base collection.
            bounds = {obj for obj in created if str(getattr(obj, "sollum_type", "")).startswith("sollumz_bound_")}
            for bound in list(bounds):
                bounds.update(child for child in bound.children_recursive if child in created)
            created.difference_update(bounds)
            for bound in bounds:
                bpy.data.objects.remove(bound, do_unlink=True)
            x, y, z, w = ref["rotation_xyzw"]
            placement = Matrix.LocRotScale(Vector(ref["position"]), Quaternion((w, x, y, z)), Vector(ref["scale"]))
            bpy.context.view_layer.update()
            roots = [obj for obj in created if obj.parent not in created]
            for obj in roots:
                obj.matrix_world = placement @ obj.matrix_world
                obj["rq_zone_archetype"] = ref["archetype"]
        for source, layer_role in collisions:
            created = load(source, definitions["collision"], "collision")
            if not created:
                raise RuntimeError(f"No collisions imported from {source.name}")
            for obj in created:
                obj["rq_zone_collision_layer"] = layer_role
        load(occlusion, definitions["collision"], "occlusion")

        if hd_cache is not None:
            for texture in set(bpy.data.images) - before_images:
                if texture.source == "FILE" and texture.filepath and not texture.packed_file:
                    texture_path = Path(bpy.path.abspath(texture.filepath)).resolve()
                    if hd_cache in texture_path.parents:
                        texture.pack()

        instances = []
        warnings = json.dumps(manifest.get("warnings", []) + import_warnings, ensure_ascii=False)
        for role, collection in definitions.items():
            collection.asset_mark()
            collection.asset_data.description = f"Zone package {name}: {role}. Shared origin."
            collection.use_fake_user = True
            instance = bpy.data.objects.new(f"{collection.name}_instance", None)
            scene.collection.objects.link(instance)
            instance.instance_type = "COLLECTION"
            instance.instance_collection = collection
            instance.matrix_basis = Matrix.Identity(4)
            instance["rq_zone_role"] = role
            instance["rq_zone_package"] = name
            instance["rq_zone_manifest"] = str(path)
            instance["rq_zone_warnings"] = warnings
            instance["rq_zone_source_pivot"] = manifest["pivot_world"]
            if hd_cache is not None:
                instance["rq_zone_hd_cache"] = str(hd_cache)
            instances.append(instance)
            if role != "base":
                instance.hide_set(True)
                instance.hide_render = True

        source_group = link_zone_sources(scene, definitions.values(), name)

        # Only clean empty containers created by these imports.
        keep = set(definitions.values()) | {source_group}
        for collection in list(set(bpy.data.collections) - before_collections - keep):
            if not collection.objects and not collection.children:
                bpy.data.collections.remove(collection)
        for obj in bpy.context.selected_objects:
            obj.select_set(False)
        instances[0].select_set(True)
        bpy.context.view_layer.objects.active = instances[0]
        return instances
    except Exception:
        # Roll back only datablocks introduced by this import. Existing scene data stays intact.
        for obj in set(bpy.data.objects) - before_objects:
            bpy.data.objects.remove(obj, do_unlink=True)
        for collection in set(bpy.data.collections) - before_collections:
            bpy.data.collections.remove(collection)
        for obj in previous_selection:
            if obj.name in bpy.context.view_layer.objects:
                obj.select_set(True)
        bpy.context.view_layer.objects.active = previous_active
        raise
    finally:
        bpy.context.view_layer.active_layer_collection = previous_active_layer


class RQ_OT_import_zone_package(bpy.types.Operator, ImportHelper):
    bl_idname = "ragequit.import_zone_package"
    bl_label = "Import CodeWalker Zone Package"
    bl_options = {"REGISTER", "UNDO"}
    filename_ext = ".json"
    filter_glob: StringProperty(default="manifest.json;*.json", options={"HIDDEN"})
    originals_directory: StringProperty(name="Original HD XML folder", subtype="DIR_PATH",
        description="Optional folder of original YDR/YFT XML and textures; leave empty to fetch through the RageQuit API")
    use_api: BoolProperty(name="Fetch HD via RageQuit API", default=True,
        description="When no XML folder is selected, fetch referenced models and textures through the API used by RageQuit")
    api_endpoint: StringProperty(name="RageQuit API address",
        description="Leave empty to use the RageQuit scene setting, or 127.0.0.1:5555 if RageQuit is not enabled")
    generate_previews: BoolProperty(name="Generate asset previews", default=True,
        description="Render thumbnails for the source collections and their instances")

    def execute(self, context):
        try:
            instances = import_package(self.filepath, self.originals_directory, use_api=self.use_api, api_endpoint=self.api_endpoint)
            warnings = json.loads(instances[0]["rq_zone_warnings"])
            if self.generate_previews:
                for instance in instances:
                    try:
                        generate_zone_asset_preview(instance)
                    except Exception as exc:
                        # A thumbnail failure must not roll back a valid import.
                        warnings.append(f"Preview for {instance.name}: {exc}")
                for instance in instances:
                    instance["rq_zone_warnings"] = json.dumps(warnings, ensure_ascii=False)
            self.report({"WARNING"} if warnings else {"INFO"},
                        f"Imported {instances[0]['rq_zone_package']}. {len(warnings)} warning(s); see instance custom property rq_zone_warnings and REPORT.txt. Collision/LOD instances are initially hidden.")
            return {"FINISHED"}
        except Exception as exc:
            self.report({"ERROR"}, str(exc))
            return {"CANCELLED"}


class RQ_OT_generate_zone_asset_preview(bpy.types.Operator):
    bl_idname = "ragequit.generate_zone_asset_preview"
    bl_label = "Generate Zone Asset Preview"
    bl_description = "Render thumbnails for selected zone instances and their editable source collections"
    bl_options = {"REGISTER", "UNDO"}

    @classmethod
    def poll(cls, context):
        return bool(selected_zone_instances(context.active_object))

    def execute(self, context):
        count, errors = 0, []
        for instance in selected_zone_instances(context.active_object, context.selected_objects):
            try:
                generate_zone_asset_preview(instance)
                count += 1
            except Exception as exc:
                errors.append(f"{instance.name}: {exc}")
        for area in context.screen.areas if context.screen else []:
            area.tag_redraw()
        if errors:
            self.report({"WARNING"}, "; ".join(errors))
        if count:
            self.report({"INFO"}, f"{count} preview(s) generated. Save the blend file to update your asset library.")
        return {"FINISHED"} if count else {"CANCELLED"}


class RQ_OT_show_zone_sources(bpy.types.Operator):
    bl_idname = "ragequit.show_zone_sources"
    bl_label = "Show Zone Package Sources"
    bl_description = "Show editable source collections; edits update all instances. Also works with older imports"
    bl_options = {"REGISTER", "UNDO"}

    @classmethod
    def poll(cls, context):
        return bool(selected_zone_instances(context.active_object))

    def execute(self, context):
        active = context.active_object
        instances = selected_zone_instances(active)
        sources = [item.instance_collection for item in instances]
        name = sources[0].get("rq_zone_package", sources[0].name)
        group = link_zone_sources(context.scene, sources, name, hide_sources=False)
        layer = find_layer_collection(context.view_layer.layer_collection, group)
        layer.exclude = False
        layer.hide_viewport = False
        # Selecting a collision/LOD instance exposes that source for editing.
        source = active.instance_collection if active.instance_type == "COLLECTION" else next(
            (coll for coll in sources if coll.get("rq_zone_role") == "base"), sources[0])
        target_layer = find_layer_collection(context.view_layer.layer_collection, source)
        target_layer.exclude = False
        target_layer.hide_viewport = False
        context.view_layer.active_layer_collection = target_layer
        self.report({"INFO"}, f"Source collection: {source.name}. Edit its objects in the Outliner; all instances update. Hide {group.name} with its eye icon when finished.")
        return {"FINISHED"}


class RQ_OT_make_zone_editable(bpy.types.Operator):
    bl_idname = "ragequit.make_zone_editable"
    bl_label = "Make Zone Package Editable"
    bl_description = "Realize selected zone instances, preserving their placement and source collections"
    bl_options = {"REGISTER", "UNDO"}
    independent_meshes: BoolProperty(name="Independent meshes", default=False)

    @classmethod
    def poll(cls, context):
        return bool(selected_zone_instances(context.active_object))

    def execute(self, context):
        instances = selected_zone_instances(context.active_object, context.selected_objects)
        if not instances:
            self.report({"WARNING"}, "No collection instances remain")
            return {"CANCELLED"}
        created_objects, created_collections = [], []
        try:
            for instance in instances:
                source = instance.instance_collection
                target = bpy.data.collections.new(source.name + "_editable")
                created_collections.append(target)
                instance.users_collection[0].children.link(target)
                transform = instance.matrix_world @ Matrix.Translation(-source.instance_offset)
                parent = instance.parent
                parent_inverse = parent.matrix_world.inverted_safe() if parent else Matrix.Identity(4)
                mapping = {}
                for original in source.all_objects:
                    if original.instance_type == "COLLECTION":
                        raise RuntimeError("Nested object instances require separate realization")
                    copied = original.copy()
                    created_objects.append(copied)
                    if self.independent_meshes and original.data is not None:
                        copied.data = original.data.copy()
                    target.objects.link(copied)
                    mapping[original] = copied
                for original, copied in mapping.items():
                    copied.parent = mapping.get(original.parent, parent)
                    copied.matrix_parent_inverse = Matrix.Identity(4)
                    if original.parent in mapping:
                        copied.matrix_basis = original.parent.matrix_world.inverted_safe() @ original.matrix_world
                    else:
                        copied.matrix_basis = parent_inverse @ transform @ original.matrix_world
                    for constraint in copied.constraints:
                        if hasattr(constraint, "target") and constraint.target in mapping:
                            constraint.target = mapping[constraint.target]
                    for modifier in copied.modifiers:
                        if hasattr(modifier, "object") and modifier.object in mapping:
                            modifier.object = mapping[modifier.object]
                    if original.get("rq_zone_role") in {"collision", "occlusion", "lod"}:
                        copied.hide_set(True)
                        copied.hide_render = True
            for instance in instances:
                bpy.data.objects.remove(instance, do_unlink=True)
        except Exception as exc:
            for obj in created_objects:
                bpy.data.objects.remove(obj, do_unlink=True)
            for collection in created_collections:
                bpy.data.collections.remove(collection)
            self.report({"ERROR"}, str(exc))
            return {"CANCELLED"}
        self.report({"INFO"}, "Package made editable. Technical objects remain hidden until enabled in the Outliner.")
        return {"FINISHED"}


def register():
    for cls in (RQ_OT_import_zone_package, RQ_OT_generate_zone_asset_preview, RQ_OT_show_zone_sources, RQ_OT_make_zone_editable):
        old = getattr(bpy.types, cls.__name__, None)
        if old is not None:
            bpy.utils.unregister_class(old)
        bpy.utils.register_class(cls)


if __name__ == "__main__":
    register()
    bpy.ops.ragequit.import_zone_package("INVOKE_DEFAULT")
