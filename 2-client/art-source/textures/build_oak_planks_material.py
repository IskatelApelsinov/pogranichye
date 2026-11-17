#!/usr/bin/env python3
"""Build seamless 1024px oak-plank PBR maps for Pogranichye.

Requires Python 3.10+, Pillow, NumPy and OpenCV.
Run from the repository root:
  python 2-client/art-source/textures/build_oak_planks_material.py

The source artwork is cropped to four complete boards. Wrap-aware feathered
patches repair both repeat axes. Normal, roughness and AO share one relief
field; URP smoothness (1 - roughness) is packed into albedo alpha.
"""
from pathlib import Path
import json
import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = HERE / "oak_planks_source.jpg"
OUT = ROOT / "2-client/game/Assets/Resources/WorldMaterials/Textures"
SIZE = 1024
CROP_X = (242, 1180)
BAND = 48
FEATHER = 20


def feather_weights(n, feather):
    w = np.ones(n, dtype=np.float32)
    ramp = 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, feather, dtype=np.float32))
    w[:feather], w[-feather:] = ramp, ramp[::-1]
    return w


def tileable_rgb(source):
    if source.width < CROP_X[1] or source.height < 256:
        raise ValueError("Unexpected oak source dimensions: %s" % (source.size,))
    crop = source.crop((CROP_X[0], 0, CROP_X[1], source.height)).convert("RGB")
    rgb = np.asarray(crop.resize((SIZE, SIZE), Image.Resampling.LANCZOS)).astype(np.float32)
    w = feather_weights(BAND * 2, FEATHER)
    c = SIZE // 2

    # Top/bottom seam: borrow a continuous strip from an interior grain row.
    shifted = np.roll(rgb, (c, 0), axis=(0, 1))
    dst = np.arange(c - BAND, c + BAND)
    src = np.arange(SIZE // 4 - BAND, SIZE // 4 + BAND) % SIZE
    patch = shifted[src, :, :].copy()
    shifted[dst] = shifted[dst] * (1.0 - w[:, None, None]) + patch * w[:, None, None]
    rgb = np.roll(shifted, (-c, 0), axis=(0, 1))

    # Left/right seam: align the repeat boundary to the internal plank joint.
    shifted = np.roll(rgb, (0, c), axis=(0, 1))
    src = np.arange(-BAND, BAND) % SIZE
    patch = shifted[:, src, :].copy()
    shifted[:, dst] = shifted[:, dst] * (1.0 - w[None, :, None]) + patch * w[None, :, None]
    return np.clip(np.roll(shifted, (0, -c), axis=(0, 1)), 0, 255).astype(np.uint8)


def blur_wrap(field, sigma):
    tiled = np.tile(field, (3, 3))
    blurred = cv2.GaussianBlur(tiled, (0, 0), sigmaX=sigma, sigmaY=sigma)
    return blurred[SIZE:2 * SIZE, SIZE:2 * SIZE]


def edge_report(values):
    a = np.asarray(values).astype(np.int16)
    sx = float(np.abs(a[:, 0] - a[:, -1]).mean())
    sy = float(np.abs(a[0] - a[-1]).mean())
    ix = float(np.abs(a[:, 1:] - a[:, :-1]).mean())
    iy = float(np.abs(a[1:] - a[:-1]).mean())
    return {
        "horizontal_seam_to_interior_ratio": round(sx / max(ix, 1e-6), 4),
        "vertical_seam_to_interior_ratio": round(sy / max(iy, 1e-6), 4),
        "horizontal_seam_mean_levels": round(sx, 4),
        "vertical_seam_mean_levels": round(sy, 4)
    }


def save_gray(path, values):
    pixels = np.clip(values * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(pixels, mode="L").save(path, format="PNG", optimize=True)
    return pixels


def main():
    if not SOURCE.is_file():
        raise FileNotFoundError("Missing source artwork: %s" % SOURCE)
    OUT.mkdir(parents=True, exist_ok=True)
    albedo = tileable_rgb(Image.open(SOURCE))
    rgb = albedo.astype(np.float32) / 255.0
    gray = 0.2126 * rgb[:, :, 0] + 0.7152 * rgb[:, :, 1] + 0.0722 * rgb[:, :, 2]

    hp_medium = gray - blur_wrap(gray, 4.0)
    hp_fine = gray - blur_wrap(gray, 1.0)
    hp_broad = gray - blur_wrap(gray, 14.0)
    height = 0.5 + 0.72 * hp_medium + 0.28 * hp_fine + 0.12 * hp_broad

    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    strength = 5.0
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(dx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack((0.5 + 0.5 * nx * inv, 0.5 + 0.5 * ny * inv,
                       0.5 + 0.5 * nz * inv), axis=-1)
    normal_px = np.clip(normal * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(normal_px, mode="RGB").save(OUT / "oak_planks_normal.png", optimize=True)

    grain = np.abs(gray - blur_wrap(gray, 2.2))
    medium = np.abs(gray - blur_wrap(gray, 8.0))
    grain_strength = np.clip(grain / 0.11, 0.0, 1.0)
    wear = np.clip((gray - blur_wrap(gray, 18.0) - 0.006) / 0.09, 0.0, 1.0)
    rough = np.clip(0.88 + 0.045 * grain_strength + 0.035 * medium / 0.12 - 0.10 * wear,
                    0.72, 0.97)
    rough_px = save_gray(OUT / "oak_planks_roughness.png", rough)

    cavity = np.clip((blur_wrap(height, 1.8) - height) * 2.8, 0.0, 0.20)
    ao = np.clip(0.985 - cavity, 0.78, 1.0)
    ao_px = save_gray(OUT / "oak_planks_ao.png", ao)

    smoothness = np.clip(1.0 - rough, 0.03, 0.28)
    rgba = np.dstack((albedo, np.clip(smoothness * 255.0 + 0.5, 0, 255).astype(np.uint8)))
    Image.fromarray(rgba, mode="RGBA").save(OUT / "oak_planks_albedo.png", optimize=True)

    edges = {
        "albedo": edge_report(albedo), "normal": edge_report(normal_px),
        "roughness": edge_report(rough_px), "ao": edge_report(ao_px)
    }
    worst = max(max(v["horizontal_seam_to_interior_ratio"],
                    v["vertical_seam_to_interior_ratio"]) for v in edges.values())
    report = {
        "name": "Pogranichye authored weathered oak planks",
        "version": 1, "resolution": [SIZE, SIZE], "tileable": True,
        "source": "2-client/art-source/textures/oak_planks_source.jpg",
        "source_crop_x": list(CROP_X),
        "seam_repair": "feathered aligned inner plank-joint and grain strips",
        "maps": {"albedo": "oak_planks_albedo.png", "normal": "oak_planks_normal.png",
                 "roughness": "oak_planks_roughness.png", "ao": "oak_planks_ao.png"},
        "normal": {"space": "tangent", "green_channel": "positive tangent Y", "strength": strength},
        "roughness": {"range": [round(float(rough.min()), 4), round(float(rough.max()), 4)],
                      "mean": round(float(rough.mean()), 4)},
        "ao": {"range": [round(float(ao.min()), 4), round(float(ao.max()), 4)],
               "mean": round(float(ao.mean()), 4)},
        "urp": {"smoothness": "1 - roughness",
                "smoothness_storage": "linear alpha channel of _BaseMap; _SmoothnessTextureChannel = 1",
                "metallic": 0.0, "wrap": "Repeat", "mipmaps": True},
        "edge_continuity": edges, "worst_seam_ratio": round(worst, 4)
    }
    if worst > 1.35:
        raise RuntimeError("Texture seam check failed: %.4f" % worst)
    (OUT / "oak_planks_material.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
