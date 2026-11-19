import json
import tempfile
import unittest
from pathlib import Path
from validate_world_material_assets import PACKS, TEXTURES, SUFFIXES, validate, PNG


def write_png(path, width, height):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(PNG + (13).to_bytes(4, "big") + b"IHDR" +
                     width.to_bytes(4, "big") + height.to_bytes(4, "big"))


class MaterialManifestTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / TEXTURES).mkdir(parents=True)
        for key, (source, prefix) in PACKS.items():
            maps = {suffix: prefix + "_" + suffix + ".png" for suffix in SUFFIXES}
            data = {"availability": "pending", "source": source, "maps": maps}
            (self.root / TEXTURES / (key + "_material.json")).write_text(json.dumps(data))

    def tearDown(self):
        self.temp.cleanup()

    def test_clean_checkout_is_explicitly_pending(self):
        errors, pending = validate(self.root)
        self.assertEqual(errors, [])
        self.assertEqual(len(pending), 6)

    def test_complete_authored_pack_is_valid(self):
        key = "lime_plaster"
        source, prefix = PACKS[key]
        src = self.root / source
        src.parent.mkdir(parents=True, exist_ok=True)
        src.write_bytes(b"source")
        for suffix in SUFFIXES:
            write_png(self.root / TEXTURES / f"{prefix}_{suffix}.png", 1024, 1024)
        path = self.root / TEXTURES / (key + "_material.json")
        data = json.loads(path.read_text())
        data["availability"] = "available"
        path.write_text(json.dumps(data))
        errors, _ = validate(self.root)
        self.assertEqual(errors, [])

    def test_complete_authored_thatch_pack_is_valid(self):
        key = "thatch_roof"
        source, prefix = PACKS[key]
        src = self.root / source
        src.parent.mkdir(parents=True, exist_ok=True)
        src.write_bytes(b"source")
        for suffix in SUFFIXES:
            write_png(self.root / TEXTURES / f"{prefix}_{suffix}.png", 1024, 1024)
        path = self.root / TEXTURES / (key + "_material.json")
        data = json.loads(path.read_text())
        data["availability"] = "available"
        path.write_text(json.dumps(data))
        errors, _ = validate(self.root)
        self.assertEqual(errors, [])

    def test_complete_authored_forest_floor_pack_is_valid(self):
        key = "forest_floor"
        source, prefix = PACKS[key]
        src = self.root / source
        src.parent.mkdir(parents=True, exist_ok=True)
        src.write_bytes(b"source")
        for suffix in SUFFIXES:
            write_png(self.root / TEXTURES / f"{prefix}_{suffix}.png", 1024, 1024)
        path = self.root / TEXTURES / (key + "_material.json")
        data = json.loads(path.read_text())
        data["availability"] = "available"
        path.write_text(json.dumps(data))
        errors, _ = validate(self.root)
        self.assertEqual(errors, [])

    def test_available_pack_with_missing_map_fails(self):
        key = "packed_road"
        source, prefix = PACKS[key]
        src = self.root / source
        src.parent.mkdir(parents=True, exist_ok=True)
        src.write_bytes(b"source")
        for suffix in SUFFIXES[:-1]:
            write_png(self.root / TEXTURES / f"{prefix}_{suffix}.png", 1024, 1024)
        path = self.root / TEXTURES / (key + "_material.json")
        data = json.loads(path.read_text())
        data["availability"] = "available"
        path.write_text(json.dumps(data))
        errors, _ = validate(self.root)
        self.assertTrue(any("must be 1024x1024" in error for error in errors))

    def test_pending_512px_preview_is_not_promoted(self):
        key = "oak_planks"
        _, prefix = PACKS[key]
        for suffix in SUFFIXES:
            write_png(self.root / TEXTURES / f"{prefix}_{suffix}.png", 512, 512)
        errors, pending = validate(self.root)
        self.assertEqual(errors, [])
        self.assertTrue(any("oak_planks" in item and "maps=4/4" in item for item in pending))

    def test_pending_1024px_map_is_rejected(self):
        key = "mossy_stone"
        _, prefix = PACKS[key]
        write_png(self.root / TEXTURES / f"{prefix}_albedo.png", 1024, 1024)
        errors, _ = validate(self.root)
        self.assertTrue(any("1024px authored map exists" in error for error in errors))

    def test_source_path_traversal_is_rejected(self):
        key = "lime_plaster"
        path = self.root / TEXTURES / (key + "_material.json")
        data = json.loads(path.read_text())
        data["source"] = "../../outside.jpg"
        path.write_text(json.dumps(data))
        errors, _ = validate(self.root)
        self.assertTrue(any("unexpected source path" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
