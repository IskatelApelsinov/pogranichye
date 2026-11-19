#!/usr/bin/env python3
"""Validate that authored world-material manifests match committed source and PBR maps."""
from __future__ import annotations
import json
import struct
import sys
from pathlib import Path

PACKS = {
    "packed_road": ("2-client/art-source/textures/packed_road_source.jpg", "packed_road"),
    "oak_planks": ("2-client/art-source/textures/oak_planks_source.jpg", "oak_planks"),
    "mossy_stone": ("2-client/art-source/textures/stone_masonry_source.jpg", "mossy_stone"),
    "lime_plaster": ("2-client/art-source/textures/lime_plaster_source.jpg", "lime_plaster"),
    "thatch_roof": ("2-client/art-source/textures/thatch_roof_source.jpg", "thatch_roof"),
    "forest_floor": ("2-client/art-source/textures/forest_floor_source.jpg", "forest_floor"),
}
TEXTURES = Path("2-client/game/Assets/Resources/WorldMaterials/Textures")
SUFFIXES = ("albedo", "normal", "roughness", "ao")
PNG = b"\x89PNG\r\n\x1a\n"


def dimensions(path: Path):
    try:
        header = path.read_bytes()[:24]
    except OSError:
        return None
    if len(header) < 24 or header[:8] != PNG or header[12:16] != b"IHDR":
        return None
    return struct.unpack(">II", header[16:24])


def validate(root: Path):
    errors, pending = [], []
    for key, (source_rel, prefix) in PACKS.items():
        manifest_path = root / TEXTURES / (key + "_material.json")
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            errors.append(f"{key}: manifest missing/invalid: {exc}")
            continue
        status = manifest.get("availability")
        if status not in ("pending", "available"):
            errors.append(f"{key}: availability must be pending or available")
            continue
        if manifest.get("source") != source_rel:
            errors.append(f"{key}: unexpected source path")
        expected = {suffix: prefix + "_" + suffix + ".png" for suffix in SUFFIXES}
        if manifest.get("maps") != expected:
            errors.append(f"{key}: map names do not match the required four-map pack")
            continue
        source = root / source_rel
        try:
            source.resolve().relative_to(root.resolve())
        except ValueError:
            errors.append(f"{key}: source path escapes repository root")
            continue
        source_ok = source.is_file() and source.stat().st_size > 0
        sizes = {}
        for suffix, filename in expected.items():
            path = root / TEXTURES / filename
            if path.is_file():
                sizes[suffix] = dimensions(path)
                if sizes[suffix] is None:
                    errors.append(f"{key}: {filename} is not a valid PNG")
        if status == "available":
            if not source_ok:
                errors.append(f"{key}: available pack has no source artwork")
            for suffix, filename in expected.items():
                size = sizes.get(suffix)
                if size != (1024, 1024):
                    errors.append(f"{key}: {filename} must be 1024x1024, got {size}")
        else:
            if any(size == (1024, 1024) for size in sizes.values()):
                errors.append(f"{key}: 1024px authored map exists while manifest is pending")
            pending.append(f"{key}: pending; source={'yes' if source_ok else 'no'}, maps={len(sizes)}/4")
    return errors, pending


def main():
    root = Path(__file__).resolve().parents[3]
    errors, pending = validate(root)
    for item in pending:
        print("PENDING:", item)
    for item in errors:
        print("ERROR:", item, file=sys.stderr)
    if errors:
        print("World material asset validation failed:", len(errors), "error(s)", file=sys.stderr)
        return 1
    print("World material asset validation passed: six manifests checked")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
