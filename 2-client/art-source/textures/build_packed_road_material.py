#!/usr/bin/env python3
"""Build an authored, tileable packed-earth PBR set for Pogranichye.

Requirements: Python 3.10+, Pillow, NumPy, OpenCV (opencv-python).
Run from the repository root:
  python 2-client/art-source/textures/build_packed_road_material.py

The source is original generated artwork. The seam repair uses feathered,
interior texture patches rather than a blurred/inpainted border band. Normal,
roughness and AO are derived from the same periodic detail field. URP
smoothness (1 - roughness) is packed into the linear albedo alpha channel.
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
SEAM_BAND = 96
SEAM_FEATHER = 40


def _feather_weights(length: int, feather: int) -> np.ndarray:
    weights = np.ones(length, dtype=np.float32)
    feather = min(feather, length // 2)
    if feather <= 0:
        return weights
    ramp = 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, feather, dtype=np.float32))
    weights[:feather] = ramp
    weights[-feather:] = ramp[::-1]
    return weights


def tileable_rgb(source: Image.Image) -> np.ndarray:
    """Move source borders to the centre, then replace seams with soft interior patches.

    Inpainting produced a low-detail dark band on this particular artwork.
    A deterministic patch blend keeps real pebbles/soil detail crossing the
    repeat boundary; the feathered patch edges are far enough from the seam.
    """
    rgb = np.asarray(source.convert("RGB").resize(
        (SIZE, SIZE), Image.Resampling.LANCZOS
    ))
    shifted = np.roll(rgb, shift=(SIZE // 2, SIZE // 2), axis=(0, 1)).astype(np.float32)
    center = SIZE // 2
    band = SEAM_BAND
    weights = _feather_weights(2 * band, SEAM_FEATHER)

    # Vertical seam: use a continuous strip from an interior source location.
    source_x = SIZE // 4
    source_columns = (np.arange(source_x - band, source_x + band) % SIZE)
    destination_columns = np.arange(center - band, center + band)
    vertical_patch = shifted[:, source_columns, :].copy()
    shifted[:, destination_columns, :] = (
        shifted[:, destination_columns, :] * (1.0 - weights[None, :, None])
        + vertical_patch * weights[None, :, None]
    )

    # Horizontal seam is sampled after the vertical repair so the cross remains
    # continuous in both directions.
    source_y = (SIZE * 3) // 4
    source_rows = (np.arange(source_y - band, source_y + band) % SIZE)
    destination_rows = np.arange(center - band, center + band)
    horizontal_patch = shifted[source_rows, :, :].copy()
    shifted[destination_rows, :, :] = (
        shifted[destination_rows, :, :] * (1.0 - weights[:, None, None])
        + horizontal_patch * weights[:, None, None]
    )

    return np.roll(
        np.clip(shifted, 0.0, 255.0).astype(np.uint8),
        shift=(-SIZE // 2, -SIZE // 2), axis=(0, 1)
    )


def gaussian_wrap(field: np.ndarray, sigma: float) -> np.ndarray:
    # Filter a 3x3 repeated field, then crop its centre to preserve periodic edges.
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

    # Fine and medium luminance relief follows pebbles and compacted soil texture.
    gray = 0.2126 * rgb[:, :, 0] + 0.7152 * rgb[:, :, 1] + 0.0722 * rgb[:, :, 2]
    hp_medium = gray - gaussian_wrap(gray, 3.0)
    hp_fine = gray - gaussian_wrap(gray, 0.8)
    height = 0.5 + 0.65 * hp_medium + 0.20 * hp_fine

    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    strength = 4.5
    nx = -dx * strength
    ny = dy * strength  # Image rows run down; tangent-space +Y runs up.
    nz = np.ones_like(nx)
    inv_len = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack((
        0.5 + 0.5 * nx * inv_len,
        0.5 + 0.5 * ny * inv_len,
        0.5 + 0.5 * nz * inv_len
    ), axis=-1)
    Image.fromarray(
        np.clip(normal * 255.0 + 0.5, 0, 255).astype(np.uint8), mode="RGB"
    ).save(OUT / "packed_road_normal.png", format="PNG", optimize=True)

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
    albedo_rgba = np.dstack((
        albedo, np.clip(smoothness * 255.0 + 0.5, 0, 255).astype(np.uint8)
    ))
    Image.fromarray(albedo_rgba, mode="RGBA").save(
        OUT / "packed_road_albedo.png", format="PNG", optimize=True
    )

    edge_x = np.abs(albedo[:, 0].astype(np.int16) - albedo[:, -1].astype(np.int16))
    edge_y = np.abs(albedo[0].astype(np.int16) - albedo[-1].astype(np.int16))
    interior_x = np.abs(albedo[:, 1:].astype(np.int16) - albedo[:, :-1].astype(np.int16))
    interior_y = np.abs(albedo[1:, :].astype(np.int16) - albedo[:-1, :].astype(np.int16))
    seam_x_mean = float(edge_x.mean())
    seam_y_mean = float(edge_y.mean())
    interior_x_mean = float(interior_x.mean())
    interior_y_mean = float(interior_y.mean())
    report = {
        "name": "Pogranichye authored packed earth",
        "version": 2,
        "resolution": [SIZE, SIZE],
        "tileable": True,
        "seam_method": "feathered interior patch blend",
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
        "edge_continuity": {
            "horizontal_seam_mean_rgb_levels": round(seam_x_mean, 4),
            "vertical_seam_mean_rgb_levels": round(seam_y_mean, 4),
            "horizontal_interior_mean_rgb_levels": round(interior_x_mean, 4),
            "vertical_interior_mean_rgb_levels": round(interior_y_mean, 4),
            "horizontal_seam_to_interior_ratio": round(seam_x_mean / max(interior_x_mean, 1e-6), 4),
            "vertical_seam_to_interior_ratio": round(seam_y_mean / max(interior_y_mean, 1e-6), 4)
        }
    }
    (OUT / "packed_road_material.json").write_text(
        json.dumps(report, indent=2), encoding="utf-8"
    )
    print("Generated packed-road PBR maps in", OUT)
    print("Edge continuity:", report["edge_continuity"])


if __name__ == "__main__":
    main()
