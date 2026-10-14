"""Blender checks for HD API resolution, plus optional live package import.

Usage: blender -b --factory-startup --python this.py -- ADDON_DIR CACHE_PARENT [PACKAGE_DIR [API_ADDRESS]]
"""
import importlib.util
import json
import sys
from pathlib import Path
from unittest.mock import patch

import bpy
from mathutils import Matrix, Quaternion, Vector

args = sys.argv[sys.argv.index("--") + 1:]
sys.path.insert(0, args[0])
bpy.ops.preferences.addon_enable(module="Sollumz")
cache_parent = Path(args[1]).resolve()
cache_parent.mkdir(parents=True, exist_ok=True)
spec = importlib.util.spec_from_file_location("zone_import", Path(__file__).with_name("import_zone_package.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
module.register()
references = [
    {"archetype": "cube", "asset_name": "cube", "asset_type": "drawable"},
    {"archetype": "cube", "asset_name": "cube", "asset_type": "drawable"},
    {"archetype": "lamp", "asset_name": "missing_alias", "asset_type": "fragment"},
]
requests = []


def fake_api(endpoint, route, payload, timeout):
    requests.append((route, payload))
    if route == "/search-files":
        return [r"base.rpf\cube.ydr", r"patch.rpf\cube.ydr", r"props.rpf\lamp.yft", r"props.rpf\lamp.ydr"]
    assert route == "/download-files"
    assert payload["Xml"] and payload["Textures"]
    assert payload["FullPaths"] == [r"base.rpf\cube.ydr", r"props.rpf\lamp.yft"]
    directory = Path(payload["Output"])
    for name in ("cube.ydr.xml", "lamp.yft.xml"):
        (directory / name).write_text("<test/>")
    return [{"results": [{"message": "XML saved successfully."}]}]


before = set(bpy.data.objects)
with patch.object(module, "api_post", fake_api):
    originals, warnings, cache = module.resolve_api_originals(references, "127.0.0.1:5555", cache_parent)
assert len(originals) == 3 and originals[0][0] == originals[1][0]
assert originals[2][0].name == "lamp.yft.xml", "Fragment/fallback name resolution failed"
assert len(warnings) == 1 and "base.rpf" in warnings[0], "Ambiguity not reported"
assert len(requests) == 2, "Repeated occurrences caused repeated downloads"
assert (cache / "sources.json").is_file()
for result, message in (([], "HD models not found"), ({"error": "bad"}, "Unexpected RageQuit search response")):
    with patch.object(module, "api_post", return_value=result) as api:
        try:
            module.resolve_api_originals(references, cache_parent=cache_parent)
            raise AssertionError("Invalid search accepted")
        except ValueError as exc:
            assert message in str(exc), str(exc)
        assert api.call_count == 1, "Download attempted after search failure"
with patch.object(module, "api_post", side_effect=[[r"base.rpf\cube.ydr"], [{"results": [{"error": "fixture failure"}]}]]):
    try:
        module.resolve_api_originals(references[:1], cache_parent=cache_parent)
        raise AssertionError("Extraction error accepted")
    except RuntimeError as exc:
        assert "fixture failure" in str(exc)
with patch.object(module.request, "build_opener") as opener:
    opener.return_value.open.side_effect = module.error.URLError("offline")
    try:
        module.api_post("127.0.0.1:5555", "/search-files", {}, 1)
        raise AssertionError("Offline API accepted")
    except RuntimeError as exc:
        assert "Start the API service" in str(exc) and "Original HD XML folder" in str(exc)
assert set(bpy.data.objects) == before, "HD preflight changed the scene"
print("PASS: API deduplication, type/name resolution, source ambiguity, missing models, malformed response, extraction failure, offline API, scene preservation", flush=True)

if len(args) > 2:
    package = Path(args[2])
    manifest = json.loads((package / "manifest.json").read_text(encoding="utf-8-sig"))
    instances = module.import_package(package, api_endpoint=args[3] if len(args) > 3 else None, cache_parent=cache_parent)
    assert all(obj["rq_zone_package"] == manifest["name"] for obj in instances), "HD lookup changed the package name"
    assert len(instances) == 3
    assert {obj["rq_zone_role"] for obj in instances} == {"base", "collision", "lod"}
    assert all(obj.location.length < 1e-6 for obj in instances)
    base = next(obj.instance_collection for obj in instances if obj["rq_zone_role"] == "base")
    roots = [obj for obj in base.all_objects if obj.get("rq_zone_archetype")]
    assert len(roots) == len(manifest["references"]), "HD occurrence count changed"
    bpy.context.view_layer.update()
    unmatched = list(roots)
    for ref in manifest["references"]:
        x, y, z, w = ref["rotation_xyzw"]
        transform = Matrix.LocRotScale(Vector(ref["position"]), Quaternion((w, x, y, z)), Vector(ref["scale"]))
        matches = [obj for obj in unmatched if obj["rq_zone_archetype"] == ref["archetype"]
                   and max(abs(obj.matrix_world[r][c] - transform[r][c]) for r in range(4) for c in range(4)) < .001]
        assert matches, f"Wrong placement: {ref['archetype']} at {ref['position']}"
        unmatched.remove(matches[0])
    assert not any(str(getattr(obj, "sollum_type", "")).startswith("sollumz_bound_") for obj in base.all_objects)
    assert before.issubset(set(bpy.data.objects))
    hd_cache = Path(instances[0]["rq_zone_hd_cache"])
    textures = [tex for tex in bpy.data.images if tex.filepath and hd_cache in Path(bpy.path.abspath(tex.filepath)).resolve().parents]
    assert textures and all(tex.packed_file for tex in textures), "HD textures are not packed"
    print(f"PASS: real package {manifest['name']}, {len(roots)} HD occurrences, three instances, original placements, {len(textures)} packed HD textures", flush=True)
