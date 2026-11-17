#!/usr/bin/env python3
"""Build authored seamless 1024px mossy fieldstone PBR maps for Pogranichye.

Requires Python 3.10+, Pillow, NumPy and OpenCV.
Run from repository root:
  python 2-client/art-source/textures/build_mossy_stone_material.py

The original wall artwork is seam-repaired on both repeat axes. Normal,
roughness and AO derive from the same relief; URP smoothness (1 - roughness)
is packed into the linear albedo alpha channel.
"""
from pathlib import Path
import json
import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = HERE / "stone_masonry_source.jpg"
OUT = ROOT / "2-client/game/Assets/Resources/WorldMaterials/Textures"
SIZE = 1024
BAND = 96
FEATHER = 40


def weights(length, feather):
    result = np.ones(length, dtype=np.float32)
    feather = min(feather, length // 2)
    ramp = 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, feather, dtype=np.float32))
    result[:feather], result[-feather:] = ramp, ramp[::-1]
    return result


def tileable_rgb(source):
    side = min(source.width, source.height)
    left, top = (source.width - side) // 2, (source.height - side) // 2
    source = source.crop((left, top, left + side, top + side)).convert("RGB")
    rgb = np.asarray(source.resize((SIZE, SIZE), Image.Resampling.LANCZOS)).astype(np.float32)
    shifted = np.roll(rgb, (SIZE // 2, SIZE // 2), axis=(0, 1))
    centre = SIZE // 2
    destination = np.arange(centre - BAND, centre + BAND)
    blend = weights(2 * BAND, FEATHER)

    # Replace the central vertical seam with an interior strip, then the horizontal seam.
    sx = SIZE // 4
    columns = np.arange(sx - BAND, sx + BAND) % SIZE
    patch = shifted[:, columns, :].copy()
    shifted[:, destination, :] = (
        shifted[:, destination, :] * (1.0 - blend[None, :, None])
        + patch * blend[None, :, None]
    )
    sy = SIZE * 3 // 4
    rows = np.arange(sy - BAND, sy + BAND) % SIZE
    patch = shifted[rows, :, :].copy()
    shifted[destination, :, :] = (
        shifted[destination, :, :] * (1.0 - blend[:, None, None])
        + patch * blend[:, None, None]
    )
    return np.roll(np.clip(shifted, 0, 255).astype(np.uint8),
                   (-SIZE // 2, -SIZE // 2), axis=(0, 1))


def blur_wrap(field, sigma):
    tiled = np.tile(field, (3, 3, 1) if field.ndim == 3 else (3, 3))
    blurred = cv2.GaussianBlur(tiled, (0, 0), sigmaX=sigma, sigmaY=sigma)
    return blurred[SIZE:2 * SIZE, SIZE:2 * SIZE]


def edge_report(values):
    a = np.asarray(values).astype(np.int16)
    sx = float(np.abs(a[:, 0] - a[:, -1]).mean())
    sy = float(np.abs(a[0] - a[-1]).mean())
    ix = float(np.abs(a[:, 1:] - a[:, :-1]).mean())
    iy = float(np.abs(a[1:] - a[:-1]).mean())
    return {
        "horizontal_seam_mean_levels": round(sx, 4),
        "vertical_seam_mean_levels": round(sy, 4),
        "horizontal_seam_to_interior_ratio": round(sx / max(ix, 1e-6), 4),
        "vertical_seam_to_interior_ratio": round(sy / max(iy, 1e-6), 4)
    }


def save_gray(path, values):
    pixels = np.clip(values * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(pixels, mode="L").save(path, format="PNG", optimize=True)
    return pixels


def main():
    if not SOURCE.is_file():
        raise FileNotFoundError("Missing source artwork: %s" % SOURCE)
    OUT.mkdir(parents=True, exist_ok=True)
    source_rgb = tileable_rgb(Image.open(SOURCE)).astype(np.float32) / 255.0

    # Remove only broad illumination drift while preserving authored mineral colour.
    broad = blur_wrap(source_rgb, 72.0)
    target = np.median(broad.reshape(-1, 3), axis=0)
    corrected = np.clip(source_rgb * (target[None, None, :] / np.maximum(broad, 0.05)), 0, 1)
    albedo = np.clip(0.65 * source_rgb + 0.35 * corrected, 0, 1)
    gray = 0.2126 * albedo[:, :, 0] + 0.7152 * albedo[:, :, 1] + 0.0722 * albedo[:, :, 2]

    # Shared relief: raised stone faces, recessed mortar and fine mineral grain.
    height = np.clip(
        0.50 + 0.78 * (gray - blur_wrap(gray, 8.0))
        + 0.22 * (gray - blur_wrap(gray, 1.3))
        + 0.16 * (gray - blur_wrap(gray, 22.0)), 0, 1
    )
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    strength = 5.2
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(dx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack((0.5 + 0.5 * nx * inv, 0.5 + 0.5 * ny * inv,
                       0.5 + 0.5 * nz * inv), axis=-1)
    normal_px = np.clip(normal * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(normal_px, mode="RGB").save(
        OUT / "mossy_stone_normal.png", format="PNG", optimize=True)

    moss = np.clip((albedo[:, :, 1] - albedo[:, :, 0] * 0.90) * 4.0, 0, 1)
    fine = np.abs(gray - blur_wrap(gray, 2.0))
    medium = np.abs(gray - blur_wrap(gray, 9.0))
    roughness = np.clip(
        0.91 + 0.045 * np.clip(fine / 0.10, 0, 1)
        + 0.025 * np.clip(medium / 0.12, 0, 1) - 0.018 * moss,
        0.82, 0.985)
    rough_px = save_gray(OUT / "mossy_stone_roughness.png", roughness)

    cavity = np.clip((blur_wrap(height, 2.0) - height) * 3.0, 0, 0.25)
    joints = np.clip((0.43 - gray) * 1.7, 0, 0.16)
    ao = np.clip(0.985 - cavity - joints, 0.66, 1.0)
    ao_px = save_gray(OUT / "mossy_stone_ao.png", ao)

    smoothness = np.clip(1.0 - roughness, 0.015, 0.18)
    rgba = np.dstack((np.clip(albedo * 255.0 + 0.5, 0, 255).astype(np.uint8),
                      np.clip(smoothness * 255.0 + 0.5, 0, 255).astype(np.uint8)))
    Image.fromarray(rgba, mode="RGBA").save(
        OUT / "mossy_stone_albedo.png", format="PNG", optimize=True)

    edges = {
        "albedo": edge_report(rgba[:, :, :3]),
        "normal": edge_report(normal_px),
        "roughness": edge_report(rough_px),
        "ao": edge_report(ao_px)
    }
    worst = max(max(v["horizontal_seam_to_interior_ratio"],
                    v["vertical_seam_to_interior_ratio"]) for v in edges.values())
    decoded = normal_px.astype(np.float32) / 127.5 - 1.0
    normal_length = float(np.linalg.norm(decoded, axis=2).mean())
    if worst > 1.35:
        raise RuntimeError("Texture seam check failed: %.4f" % worst)
    if not 0.98 <= normal_length <= 1.02:
        raise RuntimeError("Decoded normal vectors are not normalized: %.5f" % normal_length)

    report = {
        "name": "Pogranichye authored weathered mossy fieldstone masonry",
        "availability": "available",
        "version": 1, "resolution": [SIZE, SIZE], "tileable": True,
        "source": "2-client/art-source/textures/stone_masonry_source.jpg",
        "seam_method": "feathered interior masonry patches on both axes",
        "maps": {"albedo": "mossy_stone_albedo.png", "normal": "mossy_stone_normal.png",
                 "roughness": "mossy_stone_roughness.png", "ao": "mossy_stone_ao.png"},
        "normal": {"space": "tangent", "green_channel": "positive tangent Y",
                   "strength": strength, "decoded_mean_length": round(normal_length, 5)},
        "roughness": {"range": [round(float(roughness.min()), 4), round(float(roughness.max()), 4)],
                      "mean": round(float(roughness.mean()), 4)},
        "ao": {"range": [round(float(ao.min()), 4), round(float(ao.max()), 4)],
               "mean": round(float(ao.mean()), 4)},
        "urp": {"smoothness": "1 - roughness",
                "smoothness_storage": "linear alpha channel of _BaseMap; _SmoothnessTextureChannel = 1",
                "metallic": 0.0, "wrap": "Repeat", "mipmaps": True},
        "edge_continuity": edges, "worst_seam_ratio": round(worst, 4)
    }
    (OUT / "mossy_stone_material.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
