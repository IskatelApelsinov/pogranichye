#!/usr/bin/env python3
"""Generate seamless, deterministic PBR world materials for Pogranichye.

Run with Blender 4.2+:
  blender --background --python generate_world_materials.py

The script writes Unity-ready PNG maps directly into:
  2-client/game/Assets/Art/Generated/WorldMaterials/Textures/

All source fields are periodic, so opposite texture borders match. Maps are
created from one shared height field: Albedo, tangent-space Normal, Roughness,
and AO. This is a stylized-realistic base pass, not scanned photogrammetry.
"""
import json
import math
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "2-client" / "game" / "Assets" / "Resources" / "WorldMaterials"
TEXTURES = OUT / "Textures"
SIZE = 512
TAU = math.tau


def clamp(v, lo=0.0, hi=1.0):
    return max(lo, min(hi, v))


def wave(x, y, fx, fy, phase=0.0):
    return math.sin(TAU * (fx * x + fy * y) + phase)


def periodic_noise(x, y, seed):
    # Integer-frequency harmonics guarantee exact periodicity at tile borders.
    s = seed * 0.173
    return (
        0.46 * wave(x, y, 3 + seed % 3, 2 + seed % 4, s) +
        0.27 * wave(x, y, 7 + seed % 5, 5 + seed % 3, s * 1.7) +
        0.17 * wave(x, y, 13 + seed % 7, 11 + seed % 5, s * 2.3) +
        0.10 * wave(x, y, 23 + seed % 11, 19 + seed % 7, s * 3.1)
    )


def material_sample(kind, x, y):
    n = periodic_noise(x, y, 17)
    fine = periodic_noise(x, y, 41)
    if kind == "meadow_soil":
        grass = 0.5 + 0.5 * wave(x, y, 5, 4, 0.8) * wave(x, y, 3, 7, 1.3)
        height = 0.48 + 0.13*n + 0.035*fine + 0.035*grass
        dark = clamp((0.34 - n - fine*0.25) * 1.8)
        green = (0.25 + 0.045*n, 0.34 + 0.055*n, 0.17 + 0.035*n)
        earth = (0.30 + 0.035*n, 0.235 + 0.025*n, 0.15 + 0.02*n)
        color = tuple(green[i]*(1-dark*0.68) + earth[i]*dark*0.68 for i in range(3))
        rough = 0.88 - 0.08*grass
    elif kind == "oak_bark":
        grain = wave(x, y, 2, 31, 0.4) + 0.42*wave(x, y, 5, 67, 1.1) + 0.18*wave(x, y, 9, 113, 2.0)
        height = 0.48 + grain*0.12 + n*0.045
        color = (0.25 + grain*0.045, 0.145 + grain*0.032, 0.075 + grain*0.02)
        rough = 0.91
    elif kind == "oak_planks":
        grain = wave(x, y, 2, 17, 0.2) + 0.35*wave(x, y, 5, 37, 1.8) + 0.16*wave(x, y, 11, 73, 0.7)
        seam = clamp((0.13 - abs(wave(x, y, 0, 5, 0.0))) * 3.0)
        height = 0.52 + grain*0.075 + n*0.025 - seam*0.10
        color = (0.34 + grain*0.055 - seam*0.13, 0.215 + grain*0.04 - seam*0.09, 0.115 + grain*0.025 - seam*0.045)
        rough = 0.78 + seam*0.08
    elif kind == "mossy_stone":
        cells = wave(x, y, 4, 3, 0.3) * wave(x, y, 3, 5, 1.1)
        height = 0.50 + abs(n)*0.14 + cells*0.035
        moss = clamp((n*0.7 + fine*0.4 - 0.28)*1.8)
        stone = (0.37 + n*0.055, 0.36 + n*0.05, 0.32 + n*0.045)
        green = (0.20 + n*0.025, 0.27 + n*0.035, 0.13 + n*0.02)
        color = tuple(stone[i]*(1-moss*0.72) + green[i]*moss*0.72 for i in range(3))
        rough = 0.94
    elif kind == "forest_floor":
        leaf = 0.5 + 0.5*wave(x, y, 8, 5, 1.4)*wave(x, y, 5, 9, 0.3)
        height = 0.43 + n*0.09 + leaf*0.035
        color = (0.24 + n*0.035 + leaf*0.025, 0.20 + n*0.03 + leaf*0.035, 0.12 + n*0.02)
        rough = 0.96
    elif kind == "leaf_canopy":
        vein = abs(wave(x, y, 7, 2, 0.25) + 0.34*wave(x, y, 13, 4, 1.1))
        lobes = 0.5 + 0.5*wave(x, y, 9, 7, 0.6)*wave(x, y, 5, 11, 1.4)
        height = 0.48 + lobes*0.12 - vein*0.025 + n*0.025
        color = (0.12 + lobes*0.075 + n*0.025, 0.23 + lobes*0.12 + n*0.035, 0.075 + lobes*0.045 + n*0.018)
        rough = 0.86
    elif kind == "thatch_roof":
        straw = wave(x, y, 3, 47, 0.2) + 0.42*wave(x, y, 7, 91, 1.6) + 0.18*wave(x, y, 13, 137, 0.8)
        bundles = 0.5 + 0.5*wave(x, y, 0, 9, 0.3)
        height = 0.48 + straw*0.065 + bundles*0.055 + n*0.018
        color = (0.39 + straw*0.075 + bundles*0.035, 0.285 + straw*0.06 + bundles*0.025, 0.14 + straw*0.035)
        rough = 0.97
    elif kind == "packed_road":
        grit = abs(fine)*0.7 + abs(n)*0.3
        ruts = abs(wave(x, y, 2, 0, 0.4) + 0.22*wave(x, y, 5, 0, 1.7))
        height = 0.47 + grit*0.09 - ruts*0.045
        color = (0.31 + grit*0.075 - ruts*0.025, 0.235 + grit*0.055 - ruts*0.02, 0.155 + grit*0.035)
        rough = 0.95
    elif kind == "cobblestone":
        cx, cy = (x*7.0 + 0.5) % 1.0 - 0.5, (y*7.0 + 0.5) % 1.0 - 0.5
        radius = math.sqrt((cx*1.04)**2 + (cy*0.94)**2)
        joint = clamp((radius - 0.34) * 18.0)
        crown = max(0.0, 1.0 - radius/0.43)
        variation = n*0.55 + fine*0.25
        height = 0.36 + crown*0.19 - joint*0.10 + variation*0.025
        stone = (0.36 + variation*0.08, 0.345 + variation*0.075, 0.30 + variation*0.07)
        mortar = (0.19, 0.18, 0.15)
        color = tuple(stone[i]*(1-joint*0.92) + mortar[i]*(joint*0.92) for i in range(3))
        rough = 0.91
    elif kind == "lime_plaster":
        mottling = n*0.65 + fine*0.35
        height = 0.50 + mottling*0.025 + abs(fine)*0.025
        color = (0.62 + mottling*0.055, 0.56 + mottling*0.05, 0.43 + mottling*0.045)
        rough = 0.9
    else:
        raise ValueError(kind)
    return tuple(clamp(c) for c in color), clamp(height), clamp(rough)


def make_image(name, pixels, colorspace):
    image = bpy.data.images.new(name, width=SIZE, height=SIZE, alpha=True, float_buffer=False)
    image.pixels.foreach_set(pixels)
    image.file_format = "PNG"
    image.filepath_raw = str(TEXTURES / (name.lower().replace(" ", "_") + ".png"))
    try:
        image.colorspace_settings.name = colorspace
    except Exception:
        pass
    image.save()
    image.pack()
    return image.filepath_raw


def build_material(kind):
    count = SIZE * SIZE
    colors = [None] * count
    heights = [0.0] * count
    roughness = [0.0] * count
    for y in range(SIZE):
        v = y / SIZE
        for x in range(SIZE):
            u = x / SIZE
            i = y * SIZE + x
            c, h, r = material_sample(kind, u, v)
            colors[i], heights[i], roughness[i] = c, h, r

    albedo, normal, rough, ao = [], [], [], []
    strength = 3.2
    for y in range(SIZE):
        ym, yp = (y-1) % SIZE, (y+1) % SIZE
        for x in range(SIZE):
            xm, xp = (x-1) % SIZE, (x+1) % SIZE
            i = y * SIZE + x
            c = colors[i]
            albedo.extend((*c, 1.0))
            dx = (heights[y*SIZE+xp] - heights[y*SIZE+xm]) * strength
            dy = (heights[yp*SIZE+x] - heights[ym*SIZE+x]) * strength
            nx, ny, nz = -dx, -dy, 1.0
            length = math.sqrt(nx*nx + ny*ny + nz*nz)
            normal.extend((0.5+0.5*nx/length, 0.5+0.5*ny/length, 0.5+0.5*nz/length, 1.0))
            rough.extend((roughness[i], roughness[i], roughness[i], 1.0))
            # Cheap local cavity approximation from height contrast; deterministic and seamless.
            cavity = clamp(0.82 + (heights[i] - (heights[y*SIZE+xm] + heights[y*SIZE+xp] + heights[ym*SIZE+x] + heights[yp*SIZE+x]) * 0.25) * 1.4)
            ao.extend((cavity, cavity, cavity, 1.0))

    prefix = kind
    return {
        "albedo": make_image(prefix + " Albedo", albedo, "sRGB"),
        "normal": make_image(prefix + " Normal", normal, "Non-Color"),
        "roughness": make_image(prefix + " Roughness", rough, "Non-Color"),
        "ao": make_image(prefix + " AO", ao, "Non-Color"),
    }


def main():
    TEXTURES.mkdir(parents=True, exist_ok=True)
    materials = {}
    for kind in ("meadow_soil", "oak_bark", "oak_planks", "mossy_stone", "forest_floor", "leaf_canopy", "thatch_roof", "packed_road", "cobblestone", "lime_plaster"):
        materials[kind] = build_material(kind)
    manifest = {
        "name": "Pogranichye World Materials",
        "version": 1,
        "generator": "generate_world_materials.py",
        "blender": bpy.app.version_string,
        "resolution": [SIZE, SIZE],
        "tileable": True,
        "maps": ["albedo", "normal", "roughness", "ao"],
        "materials": materials,
        "unity_notes": {
            "albedo": "sRGB on",
            "normal": "Texture Type = Normal map",
            "roughness": "sRGB off; use a shader that reads roughness or invert for smoothness",
            "ao": "sRGB off",
            "wrap": "Repeat",
            "mipmaps": True
        }
    }
    (OUT / "material_manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print("Generated %d seamless PBR materials in %s" % (len(materials), TEXTURES))


if __name__ == "__main__":
    main()
