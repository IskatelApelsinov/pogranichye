#!/usr/bin/env python3
"""Validate generated tavern props inside Blender after running the generator.

Run from this directory:
    blender --background --python validate_tavern_props.py
"""
import json
import math
from pathlib import Path

import bpy
from mathutils import Vector


HERE = Path(__file__).resolve().parent
OUTPUT = HERE / "generated"
TEXTURE_DIR = OUTPUT / "textures"
EXPECTED_ROOTS = {
    "PROP_Tankard_OakBeer",
    "PROP_Barrel_Oak",
    "PROP_Basket_WickerMarket",
    "PROP_Bench_Rustic",
}
EXPECTED_TEXTURES = ("oak_grain.png", "wicker_reed.png", "bread_crust.png")


def require(condition, message):
    if not condition:
        raise RuntimeError("[Pogranichye validation] " + message)


def close(a, b, tolerance=1e-4):
    return abs(a - b) <= tolerance


def validate():
    require(bpy.app.version >= (4, 2, 0), "Blender 4.2 or newer is required")
    manifest_path = OUTPUT / "asset_manifest.json"
    blend_path = OUTPUT / "Pogranichye_Tavern_Props.blend"
    preview_path = OUTPUT / "tavern_props_preview.png"
    require(manifest_path.is_file(), "asset_manifest.json is missing")
    require(blend_path.is_file(), "Pogranichye_Tavern_Props.blend is missing")
    require(preview_path.is_file(), "tavern_props_preview.png is missing")

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    assets = manifest.get("assets", [])
    names = {asset.get("root") for asset in assets}
    require(names == EXPECTED_ROOTS, "manifest roots do not match the four expected props")
    require(manifest.get("units") == "metres", "manifest units must be metres")
    require(manifest.get("texture_size") == [128, 128], "textures must be 128x128")

    for filename in EXPECTED_TEXTURES:
        require((TEXTURE_DIR / filename).is_file(), "missing texture: " + filename)

    checked = []
    for asset in assets:
        name = asset["root"]
        root = bpy.data.objects.get(name)
        require(root is not None and root.type == "EMPTY", name + " root empty is missing")
        require(all(close(value, 0.0) for value in root.location),
                name + " root origin must remain at the ground-footprint centre")
        children = list(root.children_recursive)
        meshes = [obj for obj in children if obj.type == "MESH"]
        require(meshes, name + " has no mesh children")

        triangle_count = 0
        lowest_z = float("inf")
        highest_z = float("-inf")
        for obj in meshes:
            mesh = obj.data
            mesh.calc_loop_triangles()
            triangle_count += len(mesh.loop_triangles)
            require(len(mesh.vertices) > 0, name + " contains an empty mesh: " + obj.name)
            require(len(mesh.polygons) > 0, name + " contains a mesh without faces: " + obj.name)
            for vertex in mesh.vertices:
                point = obj.matrix_local @ vertex.co
                require(all(math.isfinite(component) for component in point),
                        name + " contains a non-finite vertex: " + obj.name)
                lowest_z = min(lowest_z, point.z)
                highest_z = max(highest_z, point.z)
            for slot in obj.material_slots:
                material = slot.material
                if material is None or not material.use_nodes:
                    continue
                has_image_texture = any(node.type == "TEX_IMAGE" and node.image is not None
                                         for node in material.node_tree.nodes)
                if has_image_texture:
                    require(len(mesh.uv_layers) > 0,
                            name + " has an image-textured mesh without UVs: " + obj.name)

        require(triangle_count == asset.get("triangle_count"),
                "%s triangle count differs from the manifest (%d != %s)" %
                (name, triangle_count, asset.get("triangle_count")))
        require(lowest_z >= -0.005, name + " extends below its ground plane")
        require(lowest_z <= 0.03, name + " is floating above its ground plane")
        require(highest_z > 0.1, name + " has an implausibly small height")
        require(asset.get("within_triangle_budget") is True,
                name + " is outside the declared 300-2000 triangle target")

        exports = asset.get("exports", [])
        require(exports, name + " has no successful interchange export")
        stem = name.replace("PROP_", "").lower()
        for extension in exports:
            require(extension in ("fbx", "glb"), name + " lists an unknown export format")
            require((OUTPUT / (stem + "." + extension)).is_file(),
                    name + " lists a missing " + extension + " export")

        checked.append((name, triangle_count, lowest_z, highest_z, ",".join(exports)))

    for image_name in ("Oak grain", "Wicker reed", "Bread crust"):
        image = bpy.data.images.get(image_name)
        require(image is not None, "packed image is missing: " + image_name)
        require(image.size[0] == 128 and image.size[1] == 128,
                image_name + " is not 128x128")
        require(image.packed_file is not None, image_name + " is not packed into the .blend")

    print("[Pogranichye validation] PASS — %d props" % len(checked))
    for name, triangles, low, high, exports in checked:
        print("  %s: %d tris, Z %.3f..%.3f m, exports %s" %
              (name, triangles, low, high, exports))


if __name__ == "__main__":
    validate()
