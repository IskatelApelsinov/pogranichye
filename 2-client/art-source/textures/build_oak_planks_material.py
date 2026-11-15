#!/usr/bin/env python3
"""Build an authored, seamless oak-plank PBR set for Pogranichye.

Requirements: Python 3.10+, Pillow, NumPy, OpenCV (opencv-python).
Run from the repository root:
  python 2-client/art-source/textures/build_oak_planks_material.py

The source is original generated artwork. Four full boards are cropped between
matching board joints; wrap-aware feathered interior patches repair both axes.
Normal, roughness and AO are derived from the same relief field. URP smoothness
(1 - roughness) is packed into the linear alpha channel of albedo.
"""
from pathlib import Path
import json

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = HERE / "oak_planks_source.jpg"
OUT = ROOT / "2-client" / "game" / "Assets" / "Resources" / "WorldMaterials" / "Textures"
SIZE = 1024
CROP_X = (242, 1180)  # Four complete boards, bounded by deep board joints in the source.
SEAM_BAND = 48
SEAM_FEATHER = 20


def feather_weights(length: int, feather: int) -> np.ndarray:
    weights = np.ones(length, dtype=np.float32)
    feather = min(feather, length // 2)
    if feather <= 0:
        return weights
    ramp = 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, feather, dtype=np.float32))
    weights[:feather] = ramp
    weights[-feather:] = ramp[::-1]
    return weights


def tileable_rgb(source: Image.Image) -> np.ndarray:
    """Crop complete planks and repair the repeat seams using aligned inner joints."""
    left, right = CROP_X
    if source.width < right or source.height < 256:
        raise ValueError(f"Unexpected source dimensions: {source.size}")
    crop = source.crop((left, 0, right, source.height)).convert("RGB")
    rgb = np.asarray(crop.resize((SIZE, SIZE), Image.Resampling.LANCZOS)).astype(np.float32)
    band = SEAM_BAND
    weights = feather_weights(2 * band, SEAM_FEATHER)
    center = SIZE // 2

    # Top/bottom: move the outer seam to the centre, then borrow a continuous
    # strip from an interior row. This keeps the vertical grain direction.
    shifted = np.roll(rgb, shift=(center, 0), axis=(0, 1))
    dst = np.arange(center - band, center + band)
    src = np.arange(SIZE // 4 - band, SIZE // 4 + band) % SIZE
    patch = shifted[src, :, :].copy()
    shifted[dst, :, :] = shifted[dst, :, :] * (1.0 - weights[:, None, None]) + patch * weights[:, None, None]
    rgb = np.roll(shifted, shift=(-center, 0), axis=(0, 1))

    # Left/right: move the outer seam to the centre and borrow the aligned
    # internal plank joint (the crop contains four boards with a joint at 50%).
    shifted = np.roll(rgb, shift=(0, center), axis=(0, 1))
    dst = np.arange(center - band, center + band)
    src = np.arange(-band, band) % SIZE
    patch = shifted[:, src, :].copy()
    shifted[:, dst, :] = shifted[:, dst, :] * (1.0 - weights[None, :, None]) + patch * weights[None, :, None]
    rgb = np.roll(shifted, shift=(0, -center), axis=(0, 1))
    return np.clip(rgb, 0.0, 255.0).astype(np.uint8)


def gaussian_wrap(field: np.ndarray, sigma: float) -> np.ndarray:
    tiled = np.tile(field, (3, 3))
    blurred = cv2.GaussianBlur(tiled, (0, 0), sigmaX=sigma, sigmaY=sigma)
    return blurred[SIZE:2 * SIZE, SIZE:2 * SIZE]


def edge_continuity(values: np.ndarray) -> dict:
    data = np.asarray(values).astype(np.int16)
    seam_x = float(np.abs(data[:, 0] - data[:, -1]).mean())
    seam_y = float(np.abs(data[0] - data[-1]).mean())
    inner_x = float(np.abs(data[:, 1:] - data[:, :-1]).mean())
    inner_y = float(np.abs(data[1:] - data[:-1]).mean())
    return {
        "horizontal_seam_mean_levels": round(seam_x, 4),
        "vertical_seam_mean_levels": round(seam_y, 4),
        "horizontal_interior_mean_levels": round(inner_x, 4),
        "vertical_interior_mean_levels": round(inner_y, 4),
        "horizontal_seam_to_interior_ratio": round(seam_x / max(inner_x, 1e-6), 4),
        "vertical_seam_to_interior_ratio": round(seam_y / max(inner_y, 1e-6), 4)
    }


def save_gray(path: Path, values: np.ndarray) -> np.ndarray:
    pixels = np.clip(values * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(pixels, mode="L").save(path, format="PNG", optimize=True)
    return pixels


def main() -> None:
    if not SOURCE.is_file():
        raise FileNotFoundError(f"Missing source artwork: {SOURCE}")
    OUT.mkdir(parents=True, exist_ok=True)

    albedo = tileable_rgb(Image.open(SOURCE))
    rgb = albedo.astype(np.float32) / 255.0
    gray = 0.2126 * rgb[:, :, 0] + 0.7152 * rgb[:, :, 1] + 0.0722 * rgb[:, :, 2]

    # One shared height field: long wood grain, shallow saw marks, knots and dents.
    hp_medium = gray - gaussian_wrap(gray, 4.0)
    hp_fine = gray - gaussian_wrap(gray, 1.0)
    hp_broad = gray - gaussian_wrap(gray, 14.0)
    height = 0.5 + 0.72 * hp_medium + 0.28 * hp_fine + 0.12 * hp_broad

    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    strength = 5.0
    nx = -dx * strength
    ny = dy * strength  # Image rows run down; tangent-space +Y runs up.
    nz = np.ones_like(nx)
    inv_len = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack((0.5 + 0.5 * nx * inv_len,
                       0.5 + 0.5 * ny * inv_len,
                       0.5 + 0.5 * nz * inv_len), axis=-1)
    normal_pixels = np.clip(normal * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(normal_pixels, mode="RGB").save(OUT / "oak_planks_normal.png", optimize=True)

    grain = np.abs(gray - gaussian_wrap(gray, 2.2))
    medium = np.abs(gray - gaussian_wrap(gray, 8.0))
    grain_strength = np.clip(grain / 0.11, 0.0, 1.0)
    wear = np.clip((gray - gaussian_wrap(gray, 18.0) - 0.006) / 0.09, 0.0, 1.0)
    roughness = np.clip(0.88 + 0.045 * grain_strength + 0.035 * medium / 0.12 - 0.10 * wear,
                        0.72, 0.97)
    roughness_pixels = save_gray(OUT / "oak_planks_roughness.png", roughness)

    cavity = np.clip((gaussian_wrap(height, 1.8) - height) * 2.8, 0.0, 0.20)
    ao = np.clip(0.985 - cavity, 0.78, 1.0)
    ao_pixels = save_gray(OUT / "oak_planks_ao.png", ao)

    # URP Lit: Albedo Alpha smoothness channel = 1, Metallic = 0.
    smoothness = np.clip(1.0 - roughness, 0.03, 0.28)
    albedo_rgba = np.dstack((albedo, np.clip(smoothness * 255.0 + 0.5, 0, 255).astype(np.uint8)))
    Image.fromarray(albedo_rgba, mode="RGBA").save(OUT / "oak_planks_albedo.png", optimize=True)

    report = {
        "name": "Pogranichye authored weathered oak planks",
        "version": 1,
        "resolution": [SIZE, SIZE],
        "tileable": True,
        "source": "2-client/art-source/textures/oak_planks_source.jpg",
        "source_crop_x": list(CROP_X),
        "seam_repair": "feathered, aligned inner plank-joint and grain strips",
        "maps": {
            "albedo": "oak_planks_albedo.png",
            "normal": "oak_planks_normal.png",
            "roughness": "oak_planks_roughness.png",
            "ao": "oak_planks_ao.png"
        },
        "normal": {"space": "tangent", "green_channel": "positive tangent Y", "strength": strength},
        "roughness": {"range": [round(float(roughness.min()), 4), round(float(roughness.max()), 4)],
                      "mean": round(float(roughness.mean()), 4)},
        "ao": {"range": [round(float(ao.min()), 4), round(float(ao.max()), 4)],
               "mean": round(float(ao.mean()), 4)},
        "urp": {"smoothness": "1 - roughness",
                "smoothness_storage": "linear alpha channel of _BaseMap; _SmoothnessTextureChannel = 1",
                "metallic": 0.0, "wrap": "Repeat", "mipmaps": True},
        "edge_continuity": {
            "albedo": edge_continuity(albedo),
            "normal": edge_continuity(normal_pixels),
            "roughness": edge_continuity(roughness_pixels),
            "ao": edge_continuity(ao_pixels)
        }
    }
    # Fail loudly rather than ship a visibly repeating seam.
    worst = max(max(v["horizontal_seam_to_interior_ratio"],
                    v["vertical_seam_to_interior_ratio"])
                for v in report["edge_continuity"].values())
    report["worst_seam_ratio"] = round(worst, 4)
    if worst > 1.35:
        raise RuntimeError(f"Texture seam check failed: worst ratio {worst}")
    (OUT / "oak_planks_material.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
