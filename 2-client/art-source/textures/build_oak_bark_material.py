#!/usr/bin/env python3
"""Build an authored seamless 1024px oak-bark PBR material for Pogranichye.

Requires Python 3.10+, Pillow, NumPy and OpenCV.
Run from the repository root:
  python 2-client/art-source/textures/build_oak_bark_material.py

The source is original realistic-stylized oak-bark artwork. A deterministic
Poisson patch replaces the source-image boundary cross after a half-tile
offset. All PBR maps derive from one shared relief field. URP smoothness
(1 - roughness) is packed into the linear albedo alpha channel.
"""
from __future__ import annotations

from pathlib import Path
import hashlib
import json

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = HERE / "oak_bark_source.jpg"
OUT = ROOT / "2-client/game/Assets/Resources/WorldMaterials/Textures"
SIZE = 1024
SEAM_BAND = 128
SEAM_DONOR_SHIFT = (173, 241)
MAX_SEAM_RATIO = 1.35


def blur_wrap(values: np.ndarray, sigma: float) -> np.ndarray:
    radius = max(1, int(np.ceil(sigma * 4.0)))
    padded = np.pad(values, ((radius, radius), (radius, radius)), mode="wrap")
    blurred = cv2.GaussianBlur(
        padded, (0, 0), sigmaX=sigma, sigmaY=sigma,
        borderType=cv2.BORDER_REPLICATE
    )
    return blurred[radius:-radius, radius:-radius]


def make_tileable(source: Image.Image) -> np.ndarray:
    side = min(source.width, source.height)
    left = (source.width - side) // 2
    top = (source.height - side) // 2
    square = source.crop((left, top, left + side, top + side)).convert("RGB")
    rgb = np.asarray(square.resize((SIZE, SIZE), Image.Resampling.LANCZOS))
    shifted = np.roll(rgb, (SIZE // 2, SIZE // 2), axis=(0, 1))

    # The half-tile offset moves the source's four unmatched outer edges into
    # the centre. Replace that cross with a deterministic interior donor patch.
    mask = np.zeros((SIZE, SIZE), dtype=np.uint8)
    centre = SIZE // 2
    half = SEAM_BAND // 2
    mask[centre - half:centre + half, :] = 255
    mask[:, centre - half:centre + half] = 255
    donor = np.roll(shifted, SEAM_DONOR_SHIFT, axis=(0, 1))
    repaired = cv2.seamlessClone(
        cv2.cvtColor(donor, cv2.COLOR_RGB2BGR),
        cv2.cvtColor(shifted, cv2.COLOR_RGB2BGR),
        mask, (centre, centre), cv2.NORMAL_CLONE
    )
    return cv2.cvtColor(repaired, cv2.COLOR_BGR2RGB)


def seam_ratio(values: np.ndarray) -> dict[str, float]:
    data = values.astype(np.float32)
    horizontal_edge = float(np.mean(np.abs(data[0, ...] - data[-1, ...])))
    vertical_edge = float(np.mean(np.abs(data[:, 0, ...] - data[:, -1, ...])))
    horizontal_inner = float(np.mean(np.abs(data[1:, ...] - data[:-1, ...])))
    vertical_inner = float(np.mean(np.abs(data[:, 1:, ...] - data[:, :-1, ...])))
    return {
        "horizontal": round(horizontal_edge / max(horizontal_inner, 1e-6), 4),
        "vertical": round(vertical_edge / max(vertical_inner, 1e-6), 4),
    }


def save_gray(path: Path, values: np.ndarray) -> np.ndarray:
    pixels = np.clip(values * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(pixels, mode="L").save(path, format="PNG", optimize=True)
    return pixels


def main() -> None:
    if not SOURCE.is_file():
        raise FileNotFoundError("Missing source artwork: %s" % SOURCE)
    OUT.mkdir(parents=True, exist_ok=True)

    tile = make_tileable(Image.open(SOURCE))
    rgb = tile.astype(np.float32) / 255.0

    # Remove broad source illumination drift while retaining bark hue,
    # plate boundaries and the fine fibrous surface.
    lab = cv2.cvtColor(tile, cv2.COLOR_RGB2LAB).astype(np.float32)
    lightness = lab[:, :, 0]
    illumination = blur_wrap(lightness, 52.0)
    ratio = (lightness + 1.0) / (illumination + 1.0)
    lab[:, :, 0] = np.clip(
        lightness * np.power(np.maximum(ratio, 0.25), 0.30), 0.0, 255.0
    )
    albedo_rgb = cv2.cvtColor(
        np.clip(lab, 0.0, 255.0).astype(np.uint8), cv2.COLOR_LAB2RGB
    ).astype(np.float32) / 255.0

    # Shared height: fine fissures + medium bark plates. The broad lighting
    # component is intentionally excluded so the normal does not bake shadows.
    gray = (
        0.2126 * rgb[:, :, 0] +
        0.7152 * rgb[:, :, 1] +
        0.0722 * rgb[:, :, 2]
    )
    fine = blur_wrap(gray, 1.0)
    medium = blur_wrap(gray, 6.0)
    broad = blur_wrap(gray, 22.0)
    relief = (fine - medium) * 1.65 + (medium - broad) * 0.95
    height = np.clip(0.5 + relief * 2.8, 0.12, 0.88)

    # Periodic central differences match the project's existing positive-Y
    # tangent-space normal convention.
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    nx, ny, nz = -dx * 7.5, -dy * 7.5, np.ones_like(height)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack((nx / length, ny / length, nz / length), axis=2)
    normal_pixels = np.clip(normal * 127.5 + 127.5, 0, 255).astype(np.uint8)

    local_height = blur_wrap(height, 3.5)
    cavity = np.clip(local_height - height, 0.0, 0.35)
    ridge = np.clip(height - local_height, 0.0, 0.35)
    # Dry, matte bark; recessed damp fissures are slightly rougher.
    roughness = np.clip(0.935 + cavity * 0.11 - ridge * 0.12, 0.84, 0.99)
    ao = np.clip(0.93 - cavity * 0.65, 0.70, 0.98)

    rough_pixels = save_gray(OUT / "oak_bark_roughness.png", roughness)
    ao_pixels = save_gray(OUT / "oak_bark_ao.png", ao)
    smoothness_pixels = 255 - rough_pixels
    albedo_pixels = np.dstack((
        np.clip(albedo_rgb * 255.0 + 0.5, 0, 255).astype(np.uint8),
        smoothness_pixels
    ))
    Image.fromarray(albedo_pixels, mode="RGBA").save(
        OUT / "oak_bark_albedo.png", format="PNG", optimize=True
    )
    Image.fromarray(normal_pixels, mode="RGB").save(
        OUT / "oak_bark_normal.png", format="PNG", optimize=True
    )

    edges = {
        "albedo": seam_ratio(albedo_pixels[:, :, :3]),
        "normal": seam_ratio(normal_pixels),
        "roughness": seam_ratio(rough_pixels),
        "ao": seam_ratio(ao_pixels),
    }
    worst = max(v for item in edges.values() for v in item.values())
    decoded = normal_pixels.astype(np.float32) / 127.5 - 1.0
    mean_normal_length = float(np.mean(np.linalg.norm(decoded, axis=2)))
    alpha_error = int(np.max(np.abs(
        albedo_pixels[:, :, 3].astype(np.int16) -
        (255 - rough_pixels.astype(np.int16))
    )))
    if worst > MAX_SEAM_RATIO:
        raise RuntimeError("Seam continuity failed: %.4f > %.2f" % (worst, MAX_SEAM_RATIO))
    if not 0.98 <= mean_normal_length <= 1.02:
        raise RuntimeError("Normal vectors are not normalized: %.5f" % mean_normal_length)
    if alpha_error > 1:
        raise RuntimeError("Smoothness alpha differs from inverse roughness by %d levels" % alpha_error)

    report = {
        "name": "Pogranichye authored weathered oak bark",
        "availability": "available",
        "version": 1,
        "resolution": [SIZE, SIZE],
        "tileable": True,
        "source": "2-client/art-source/textures/oak_bark_source.jpg",
        "builder": "2-client/art-source/textures/build_oak_bark_material.py",
        "maps": {
            "albedo": "oak_bark_albedo.png",
            "normal": "oak_bark_normal.png",
            "roughness": "oak_bark_roughness.png",
            "ao": "oak_bark_ao.png",
        },
        "normal": {
            "space": "tangent",
            "green_channel": "positive_y",
            "mean_decoded_length": round(mean_normal_length, 6),
        },
        "roughness": {
            "range": [round(float(roughness.min()), 4), round(float(roughness.max()), 4)]
        },
        "ao": {
            "range": [round(float(ao.min()), 4), round(float(ao.max()), 4)]
        },
        "urp": {
            "smoothness": "1 - roughness",
            "smoothness_storage": "linear albedo alpha; _SmoothnessTextureChannel = 1",
            "metallic": 0.0,
            "wrap": "Repeat",
            "mipmaps": True,
        },
        "edge_continuity": edges,
        "worst_seam_ratio": round(worst, 4),
        "alpha_vs_inverse_roughness_max_error_levels": alpha_error,
        "source_sha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    }
    (OUT / "oak_bark_material.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8"
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
