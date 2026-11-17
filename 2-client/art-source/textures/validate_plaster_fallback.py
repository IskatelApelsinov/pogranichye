#!/usr/bin/env python3
"""Validate the imported Grok plaster fallback used by the Unity world builder."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
BUILDER = ROOT / "2-client/game/Assets/_Project/Scripts/Presentation/WorldLayoutPresentationBuilder.cs"
ALBEDO = ROOT / "grok/textures/grok/plaster_wall.png"
NORMAL = ROOT / "grok/textures/grok/plaster_wall_n.png"

for asset in (ALBEDO, NORMAL):
    if not asset.is_file() or asset.stat().st_size == 0:
        raise SystemExit("Missing Grok plaster fallback asset: %s" % asset.relative_to(ROOT))

source = BUILDER.read_text(encoding="utf-8")
grok_call = 'ApplyGrokWorldMaps(_plaster, "plaster_wall", 1.5f, 1.5f);'
authored_call = 'ApplyGeneratedWorldMaps(_plaster, "lime_plaster", 1.5f, 1.5f, true);'
if grok_call not in source or authored_call not in source:
    raise SystemExit("Plaster material must wire both Grok fallback and optional authored PBR maps")
if source.index(grok_call) >= source.index(authored_call):
    raise SystemExit("Grok plaster fallback must be applied before authored PBR override")

print("PASS: Grok plaster albedo/normal exist and are applied before optional authored PBR maps")
