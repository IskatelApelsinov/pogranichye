#!/usr/bin/env python3
"""Build authored seamless 1024px aged lime-plaster PBR maps for Pogranichye.

Requires Python 3.10+, Pillow, NumPy and OpenCV.
Run from repository root:
  python 2-client/art-source/textures/build_lime_plaster_material.py

The source artwork is repaired at both repeat axes. Tangent-space normal,
roughness and AO derive from one shared relief field. URP smoothness
(1 - roughness) is packed into the linear albedo alpha channel.
"""
from pathlib import Path
import json

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = HERE / "lime_plaster_source.jpg"
OUT = ROOT / "2-client/game/Assets/Resources/WorldMaterials/Textures"
SIZE = 1024
BAND = 96
FEATHER = 36


def feather_weights(length, feather):
    result = np.ones(length, dtype=np.float32)
    ramp = 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, feather, dtype=np.float32))
    result[:feather] = ramp
    result[-feather:] = ramp[::-1]
    return result


def tileable_rgb(source):
    side = min(source.width, source.height)
    left, top = (source.width - side) // 2, (source.height - side) // 2
    square = source.crop((left, top, left + side, top + side)).convert("RGB")
    rgb = np.asarray(square.resize((SIZE, SIZE), Image.Resampling.LANCZOS)).astype(np.float32)

    # Move the original outer edges to the centre and replace each seam with
    # a colour-matched interior strip; cosine feathering preserves local detail.
    shifted = np.roll(rgb, (SIZE // 2, SIZE // 2), axis=(0, 1))
    centre = SIZE // 2
    destination = np.arange(centre - BAND, centre + BAND)
    blend = feather_weights(2 * BAND, FEATHER)

    source_x = np.arange(SIZE // 4 - BAND, SIZE // 4 + BAND) % SIZE
    patch = shifted[:, source_x, :].copy()
    target = shifted[:, destination, :]
    patch += target.mean(axis=(0, 1), keepdims=True) - patch.mean(axis=(0, 1), keepdims=True)
    shifted[:, destination, :] = target * (1.0 - blend[None, :, None]) + patch * blend[None, :, None]

    source_y = np.arange(3 * SIZE // 4 - BAND, 3 * SIZE // 4 + BAND) % SIZE
    patch = shifted[source_y, :, :].copy()
    target = shifted[destination, :, :]
    patch += target.mean(axis=(0, 1), keepdims=True) - patch.mean(axis=(0, 1), keepdims=True)
    shifted[destination, :, :] = target * (1.0 - blend[:, None, None]) + patch * blend[:, None, None]

    return np.roll(np.clip(shifted, 0, 255), (-SIZE // 2, -SIZE // 2), axis=(0, 1)).astype(np.uint8)


def blur_wrap(field, sigma):
    tiled = np.tile(field, (3, 3, 1) if field.ndim == 3 else (3, 3))
    blurred = cv2.GaussianBlur(tiled, (0, 0), sigmaX=sigma, sigmaY=sigma)
    return blurred[SIZE:2 * SIZE, SIZE:2 * SIZE]


def edge_report(values):
    array = np.asarray(values).astype(np.int16)
    horizontal = float(np.abs(array[:, 0] - array[:, -1]).mean())
    vertical = float(np.abs(array[0] - array[-1]).mean())
    inner_x = float(np.abs(array[:, 1:] - array[:, :-1]).mean())
    inner_y = float(np.abs(array[1:] - array[:-1]).mean())
    return {
        "horizontal_seam_mean_levels": round(horizontal, 4),
        "vertical_seam_mean_levels": round(vertical, 4),
        "horizontal_seam_to_interior_ratio": round(horizontal / max(inner_x, 1e-6), 4),
        "vertical_seam_to_interior_ratio": round(vertical / max(inner_y, 1e-6), 4),
    }


def save_gray(path, values):
    pixels = np.clip(values * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(pixels, mode="L").save(path, format="PNG", optimize=True)
    return pixels


def main():
    if not SOURCE.is_file():
        raise FileNotFoundError("Missing source artwork: %s" % SOURCE)
    OUT.mkdir(parents=True, exist_ok=True)
    source = tileable_rgb(Image.open(SOURCE)).astype(np.float32) / 255.0

    # Remove broad illumination drift while retaining hand-painted chalk,
    # trowel and weathering detail. A little warmth suits old rural limewash.
    broad = blur_wrap(source, 74.0)
    target = np.median(broad.reshape(-1, 3), axis=0)
    corrected = np.clip(source * (target[None, None, :] / np.maximum(broad, 0.08)), 0.0, 1.0)
    albedo = np.clip(0.62 * source + 0.38 * corrected, 0.0, 1.0)
    albedo = np.clip(albedo * np.array([1.015, 1.005, 0.975], dtype=np.float32)[None, None, :], 0.0, 1.0)

    gray = 0.2126 * albedo[:, :, 0] + 0.7152 * albedo[:, :, 1] + 0.0722 * albedo[:, :, 2]
    fine = gray - blur_wrap(gray, 1.35)
    medium = gray - blur_wrap(gray, 6.0)
    broad_relief = gray - blur_wrap(gray, 20.0)
    height = np.clip(0.50 + 0.30 * fine + 0.82 * medium + 0.18 * broad_relief, 0.0, 1.0)

    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    strength = 3.8
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(dx)
    inverse_length = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack((
        0.5 + 0.5 * nx * inverse_length,
        0.5 + 0.5 * ny * inverse_length,
        0.5 + 0.5 * nz * inverse_length,
    ), axis=-1)
    normal_pixels = np.clip(normal * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(normal_pixels, mode="RGB").save(OUT / "lime_plaster_normal.png", optimize=True)

    # Old lime plaster is very matte; trowelled ridges vary roughness subtly.
    roughness = np.clip(
        0.925 + 0.035 * np.clip(np.abs(fine) / 0.04, 0.0, 1.0)
        + 0.025 * np.clip(np.abs(medium) / 0.10, 0.0, 1.0),
        0.88, 0.995,
    )
    cavity = np.clip((blur_wrap(height, 2.0) - height) * 1.7, 0.0, 0.09)
    cracks = np.clip((0.47 - gray) * 0.16, 0.0, 0.045)
    ao = np.clip(0.995 - cavity - cracks, 0.88, 1.0)
    rough_pixels = save_gray(OUT / "lime_plaster_roughness.png", roughness)
    ao_pixels = save_gray(OUT / "lime_plaster_ao.png", ao)

    smoothness = np.clip(1.0 - roughness, 0.005, 0.12)
    albedo_rgb = np.clip(albedo * 255.0 + 0.5, 0, 255).astype(np.uint8)
    alpha = np.clip(smoothness * 255.0 + 0.5, 0, 255).astype(np.uint8)
    rgba = np.dstack((albedo_rgb, alpha))
    Image.fromarray(rgba, mode="RGBA").save(OUT / "lime_plaster_albedo.png", optimize=True)

    edges = {
        "albedo": edge_report(rgba[:, :, :3]),
        "normal": edge_report(normal_pixels),
        "roughness": edge_report(rough_pixels),
        "ao": edge_report(ao_pixels),
    }
    worst = max(
        max(item["horizontal_seam_to_interior_ratio"], item["vertical_seam_to_interior_ratio"])
        for item in edges.values()
    )
    decoded_normal = normal_pixels.astype(np.float32) / 127.5 - 1.0
    normal_length = float(np.linalg.norm(decoded_normal, axis=2).mean())
    alpha_error = int(np.abs(alpha.astype(np.int16) - (255 - rough_pixels.astype(np.int16))).max())

    if worst > 1.35:
        raise RuntimeError("Texture seam check failed: %.4f" % worst)
    if not 0.98 <= normal_length <= 1.02:
        raise RuntimeError("Decoded normal vectors are not normalized: %.5f" % normal_length)
    if alpha_error > 1:
        raise RuntimeError("URP smoothness alpha differs from inverse roughness by %d levels" % alpha_error)

    report = {
        "name": "Pogranichye aged limewashed plaster",
        "availability": "available",
        "version": 1,
        "resolution": [SIZE, SIZE],
        "tileable": True,
        "source": "2-client/art-source/textures/lime_plaster_source.jpg",
        "builder": "2-client/art-source/textures/build_lime_plaster_material.py",
        "maps": {
            "albedo": "lime_plaster_albedo.png",
            "normal": "lime_plaster_normal.png",
            "roughness": "lime_plaster_roughness.png",
            "ao": "lime_plaster_ao.png",
        },
        "normal": {"space": "tangent", "green_channel": "positive_y", "mean_decoded_length": round(normal_length, 6)},
        "roughness": {"range": [round(float(roughness.min()), 4), round(float(roughness.max()), 4)]},
        "ao": {"range": [round(float(ao.min()), 4), round(float(ao.max()), 4)]},
        "urp": {
            "smoothness": "1 - roughness",
            "smoothness_storage": "linear alpha channel of _BaseMap; _SmoothnessTextureChannel = 1",
            "metallic": 0.0,
            "wrap": "Repeat",
            "mipmaps": True,
        },
        "edge_continuity": edges,
        "worst_seam_ratio": round(worst, 4),
        "alpha_vs_inverse_roughness_max_error_levels": alpha_error,
    }
    (OUT / "lime_plaster_material.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
