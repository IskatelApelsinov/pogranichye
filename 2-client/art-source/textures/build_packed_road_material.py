#!/usr/bin/env python3
"""Build a tileable authored packed-earth PBR set for Pogranichye.

Requirements: Python 3.10+, Pillow, NumPy, OpenCV (opencv-python).
Run from the repository root:
  python 2-client/art-source/textures/build_packed_road_material.py

The source image is an original generated artwork. The script repairs the
opposite-edge seam, writes a 1024px tileable albedo, and derives consistent
tangent-space normal, roughness and AO maps. URP smoothness (1 - roughness) is
stored in the linear alpha channel of the albedo texture.
"""
from pathlib import Path
import json

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = HERE / "packed_road_source.jpg"
OUT = ROOT / "2-client" / "game" / "Assets" / "Resources" / "WorldMaterials" / "Textures"
SIZE = 1024


def tileable_rgb(source: Image.Image) -> np.ndarray:
    rgb = np.asarray(source.convert("RGB").resize((SIZE, SIZE), Image.Resampling.LANCZOS))
    # Move the original four borders to the centre and heal the cross seam.
    shifted = np.roll(rgb, shift=(SIZE // 2, SIZE // 2), axis=(0, 1))
    mask = np.zeros((SIZE, SIZE), dtype=np.uint8)
    half = 4
    c = SIZE // 2
    mask[:, c - half:c + half] = 255
    mask[c - half:c + half, :] = 255
    healed = cv2.inpaint(shifted, mask, 10, cv2.INPAINT_TELEA)
    return np.roll(healed, shift=(-SIZE // 2, -SIZE // 2), axis=(0, 1))


def gaussian_wrap(field: np.ndarray, sigma: float) -> np.ndarray:
    # Filtering a 3x3 repeat and cropping the centre gives periodic boundaries.
    tiled = np.tile(field, (3, 3))
    blurred = cv2.GaussianBlur(tiled, (0, 0), sigmaX=sigma, sigmaY=sigma)
    return blurred[SIZE:2 * SIZE, SIZE:2 * SIZE]


def save_gray(path: Path, values: np.ndarray) -> None:
    image = np.clip(values * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(image, mode="L").save(path, format="PNG", optimize=True)


def main() -> None:
    if not SOURCE.is_file():
        raise FileNotFoundError(f"Missing source artwork: {SOURCE}")
    OUT.mkdir(parents=True, exist_ok=True)
    albedo = tileable_rgb(Image.open(SOURCE))
    rgb = albedo.astype(np.float32) / 255.0

    # Luminance is used only as a fine relief guide, not as a fake colour-to-height conversion.
    gray = 0.2126 * rgb[:, :, 0] + 0.7152 * rgb[:, :, 1] + 0.0722 * rgb[:, :, 2]
    hp_medium = gray - gaussian_wrap(gray, 3.0)
    hp_fine = gray - gaussian_wrap(gray, 0.8)
    height = 0.5 + 0.65 * hp_medium + 0.20 * hp_fine

    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    strength = 4.5
    nx = -dx * strength
    ny = dy * strength  # PNG rows run down; tangent-space +Y runs up.
    nz = np.ones_like(nx)
    inv_len = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack((0.5 + 0.5 * nx * inv_len,
                       0.5 + 0.5 * ny * inv_len,
                       0.5 + 0.5 * nz * inv_len), axis=-1)
    Image.fromarray(np.clip(normal * 255.0 + 0.5, 0, 255).astype(np.uint8), mode="RGB").save(
        OUT / "packed_road_normal.png", format="PNG", optimize=True
    )

    local_detail = gray - gaussian_wrap(gray, 2.0)
    pebble = np.clip((local_detail - 0.018) / 0.13, 0.0, 1.0)
    broad = gaussian_wrap(gray, 12.0)
    damp_patch = np.clip((broad - gray - 0.012) * 3.0, 0.0, 1.0)
    roughness = np.clip(0.93 - 0.10 * pebble - 0.045 * damp_patch, 0.76, 0.98)
    save_gray(OUT / "packed_road_roughness.png", roughness)

    local_mean = gaussian_wrap(height, 1.4)
    cavity = np.clip((local_mean - height) * 2.2, 0.0, 0.16)
    ao = np.clip(0.985 - cavity, 0.82, 1.0)
    save_gray(OUT / "packed_road_ao.png", ao)

    # URP Lit reads smoothness from BaseMap alpha when _SmoothnessTextureChannel = 1.
    smoothness = np.clip(1.0 - roughness, 0.02, 0.24)
    albedo_rgba = np.dstack((albedo, np.clip(smoothness * 255.0 + 0.5, 0, 255).astype(np.uint8)))
    Image.fromarray(albedo_rgba, mode="RGBA").save(
        OUT / "packed_road_albedo.png", format="PNG", optimize=True
    )

    edge_x = np.abs(albedo[:, 0].astype(np.int16) - albedo[:, -1].astype(np.int16))
    edge_y = np.abs(albedo[0].astype(np.int16) - albedo[-1].astype(np.int16))
    report = {
        "name": "Pogranichye authored packed earth",
        "version": 1,
        "resolution": [SIZE, SIZE],
        "tileable": True,
        "source": "2-client/art-source/textures/packed_road_source.jpg",
        "maps": {
            "albedo": "packed_road_albedo.png",
            "normal": "packed_road_normal.png",
            "roughness": "packed_road_roughness.png",
            "ao": "packed_road_ao.png"
        },
        "normal": {"space": "tangent", "green_channel": "positive tangent Y", "strength": strength},
        "roughness": {
            "range": [round(float(roughness.min()), 4), round(float(roughness.max()), 4)],
            "mean": round(float(roughness.mean()), 4)
        },
        "ao": {
            "range": [round(float(ao.min()), 4), round(float(ao.max()), 4)],
            "mean": round(float(ao.mean()), 4)
        },
        "urp": {
            "smoothness": "1 - roughness",
            "smoothness_storage": "linear alpha channel of _BaseMap; _SmoothnessTextureChannel = 1",
            "metallic": 0.0,
            "wrap": "Repeat",
            "mipmaps": True
        },
        "edge_difference": {
            "horizontal_mean_rgb_levels": round(float(edge_x.mean()), 4),
            "vertical_mean_rgb_levels": round(float(edge_y.mean()), 4),
            "horizontal_max_rgb_levels": int(edge_x.max()),
            "vertical_max_rgb_levels": int(edge_y.max())
        }
    }
    (OUT / "packed_road_material.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("Wrote 4 packed-road PBR maps (smoothness in albedo alpha) to", OUT)
    print("Albedo edge differences (mean RGB levels):", report["edge_difference"])


if __name__ == "__main__":
    main()
