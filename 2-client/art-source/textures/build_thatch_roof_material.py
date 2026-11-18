#!/usr/bin/env python3
"""Build authored seamless 1024px thatched-roof PBR maps for Pogranichye.

Requires Python 3.10+, Pillow, NumPy and OpenCV.
Run from repository root:
  python 2-client/art-source/textures/build_thatch_roof_material.py

The source is hand-authored thatch artwork. A deterministic interior-patch blend
repairs the repeat seam. Normal, roughness and AO derive from one shared relief
field; URP smoothness (1 - roughness) is packed into the linear albedo alpha.
"""
from pathlib import Path
import hashlib
import json

import cv2
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = HERE / "thatch_roof_source.jpg"
OUT = ROOT / "2-client/game/Assets/Resources/WorldMaterials/Textures"
SIZE = 1024
BAND = 96
FEATHER = 40


def feather_weights(length: int, feather: int) -> np.ndarray:
    result = np.ones(length, dtype=np.float32)
    feather = min(feather, length // 2)
    ramp = 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, feather, dtype=np.float32))
    result[:feather] = ramp
    result[-feather:] = ramp[::-1]
    return result


def blur_wrap(values: np.ndarray, sigma: float) -> np.ndarray:
    radius = max(1, int(np.ceil(sigma * 4.0)))
    padded = np.pad(values, ((radius, radius), (radius, radius)), mode="wrap")
    blurred = cv2.GaussianBlur(padded, (0, 0), sigmaX=sigma, sigmaY=sigma,
                               borderType=cv2.BORDER_REPLICATE)
    return blurred[radius:-radius, radius:-radius]


def tileable_rgb(image: Image.Image) -> np.ndarray:
    side = min(image.width, image.height)
    left = (image.width - side) // 2
    top = (image.height - side) // 2
    square = image.crop((left, top, left + side, top + side)).convert("RGB")
    rgb = np.asarray(square.resize((SIZE, SIZE), Image.Resampling.LANCZOS)).astype(np.float32)
    shifted = np.roll(rgb, (SIZE // 2, SIZE // 2), axis=(0, 1))
    centre = SIZE // 2
    destination = np.arange(centre - BAND, centre + BAND)
    blend = feather_weights(2 * BAND, FEATHER)

    # Replace the original vertical edge seam now located at the centre.
    source_x = np.arange(SIZE // 4 - BAND, SIZE // 4 + BAND) % SIZE
    patch = shifted[:, source_x, :].copy()
    target = shifted[:, destination, :]
    patch += target.mean(axis=(0, 1), keepdims=True) - patch.mean(axis=(0, 1), keepdims=True)
    shifted[:, destination, :] = target * (1.0 - blend[None, :, None]) + patch * blend[None, :, None]

    # Repair the horizontal seam independently, using a different interior course.
    source_y = np.arange(SIZE * 3 // 4 - BAND, SIZE * 3 // 4 + BAND) % SIZE
    patch = shifted[source_y, :, :].copy()
    target = shifted[destination, :, :]
    patch += target.mean(axis=(0, 1), keepdims=True) - patch.mean(axis=(0, 1), keepdims=True)
    shifted[destination, :, :] = target * (1.0 - blend[:, None, None]) + patch * blend[:, None, None]
    return np.clip(shifted, 0.0, 255.0).astype(np.uint8)


def edge_ratio(values: np.ndarray) -> dict:
    values = values.astype(np.float32)
    horizontal_edge = float(np.mean(np.abs(values[0, ...] - values[-1, ...])))
    vertical_edge = float(np.mean(np.abs(values[:, 0, ...] - values[:, -1, ...])))
    horizontal_inner = float(np.mean(np.abs(values[1:, ...] - values[:-1, ...])))
    vertical_inner = float(np.mean(np.abs(values[:, 1:, ...] - values[:, :-1, ...])))
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

    rgb8 = tileable_rgb(Image.open(SOURCE))
    rgb = rgb8.astype(np.float32) / 255.0

    # Keep the warm straw palette, slightly mute orange saturation, and
    # preserve fine strand separation at close range.
    luminance = 0.2126 * rgb[:, :, 0] + 0.7152 * rgb[:, :, 1] + 0.0722 * rgb[:, :, 2]
    broad = blur_wrap(luminance, 28.0)
    median = float(np.median(broad))
    illumination = np.clip(median / np.maximum(broad, 0.08), 0.88, 1.12)
    albedo_rgb = np.clip(rgb * illumination[:, :, None], 0.0, 1.0)
    albedo_rgb[:, :, 0] *= 0.99
    albedo_rgb[:, :, 1] *= 1.015
    albedo_rgb[:, :, 2] *= 1.035
    albedo_rgb = np.clip(albedo_rgb, 0.0, 1.0)

    gray = 0.2126 * albedo_rgb[:, :, 0] + 0.7152 * albedo_rgb[:, :, 1] + 0.0722 * albedo_rgb[:, :, 2]
    local = blur_wrap(gray, 2.2)
    medium = blur_wrap(gray, 7.0)
    detail = gray - local
    course_relief = medium - blur_wrap(medium, 24.0)
    height = np.clip(0.50 + detail * 1.55 + course_relief * 0.72, 0.0, 1.0)

    # Periodic central differences avoid non-wrapping edge derivatives.
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    nx, ny, nz = -dx * 3.2, -dy * 3.2, np.ones_like(dx)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack((nx / length, ny / length, nz / length), axis=2)
    normal_pixels = np.clip(normal * 127.5 + 127.5, 0, 255).astype(np.uint8)

    fibre_contrast = np.clip(np.abs(detail) * 5.0, 0.0, 1.0)
    dark_fibre = np.clip((0.62 - gray) * 1.2, 0.0, 1.0)
    roughness = np.clip(0.865 + fibre_contrast * 0.075 + dark_fibre * 0.045, 0.82, 0.99)
    cavity = np.clip((medium - height) * 3.0, 0.0, 1.0)
    deep_gap = np.clip((0.43 - gray) * 1.6, 0.0, 1.0)
    ao = np.clip(0.985 - cavity * 0.16 - deep_gap * 0.08, 0.76, 1.0)

    smoothness = 1.0 - roughness
    albedo_rgba = np.dstack((
        np.clip(albedo_rgb * 255.0 + 0.5, 0, 255).astype(np.uint8),
        np.clip(smoothness * 255.0 + 0.5, 0, 255).astype(np.uint8)
    ))
    Image.fromarray(albedo_rgba, mode="RGBA").save(OUT / "thatch_roof_albedo.png", optimize=True)
    Image.fromarray(normal_pixels, mode="RGB").save(OUT / "thatch_roof_normal.png", optimize=True)
    rough_pixels = save_gray(OUT / "thatch_roof_roughness.png", roughness)
    ao_pixels = save_gray(OUT / "thatch_roof_ao.png", ao)

    edge = {
        "albedo": edge_ratio(albedo_rgba),
        "normal": edge_ratio(normal_pixels),
        "roughness": edge_ratio(rough_pixels),
        "ao": edge_ratio(ao_pixels),
    }
    worst = max(value for maps in edge.values() for value in maps.values())
    decoded = normal_pixels.astype(np.float32) / 127.5 - 1.0
    mean_normal_length = float(np.mean(np.linalg.norm(decoded, axis=2)))
    alpha_error = int(np.max(np.abs(
        albedo_rgba[:, :, 3].astype(np.int16) - (255 - rough_pixels.astype(np.int16))
    )))
    if worst > 1.35:
        raise RuntimeError("Texture seam check failed: %.4f" % worst)
    if not 0.98 <= mean_normal_length <= 1.02:
        raise RuntimeError("Decoded normal vectors are not normalized: %.5f" % mean_normal_length)
    if alpha_error > 1:
        raise RuntimeError("URP smoothness alpha differs from inverse roughness by %d levels" % alpha_error)

    report = {
        "name": "Pogranichye hand-laid weathered rye thatch",
        "availability": "available",
        "version": 1,
        "resolution": [SIZE, SIZE],
        "tileable": True,
        "source": "2-client/art-source/textures/thatch_roof_source.jpg",
        "builder": "2-client/art-source/textures/build_thatch_roof_material.py",
        "maps": {
            "albedo": "thatch_roof_albedo.png",
            "normal": "thatch_roof_normal.png",
            "roughness": "thatch_roof_roughness.png",
            "ao": "thatch_roof_ao.png",
        },
        "normal": {"space": "tangent", "green_channel": "positive_y",
                   "mean_decoded_length": round(mean_normal_length, 6)},
        "roughness": {"range": [round(float(roughness.min()), 4), round(float(roughness.max()), 4)]},
        "ao": {"range": [round(float(ao.min()), 4), round(float(ao.max()), 4)]},
        "urp": {
            "smoothness": "1 - roughness",
            "smoothness_storage": "linear alpha channel of _BaseMap; _SmoothnessTextureChannel = 1",
            "metallic": 0.0,
            "wrap": "Repeat",
            "mipmaps": True,
        },
        "edge_continuity": edge,
        "worst_seam_ratio": round(worst, 4),
        "alpha_vs_inverse_roughness_max_error_levels": alpha_error,
        "source_sha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    }
    (OUT / "thatch_roof_material.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
