from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
BUILDER = ROOT / "Assets/_Project/Scripts/Presentation/WorldLayoutPresentationBuilder.cs"


def extract_method_body(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:index]
    raise AssertionError(f"Unclosed method body: {signature}")


class WorldMaterialFallbackWiringTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = BUILDER.read_text(encoding="utf-8")

    def test_lime_plaster_uses_authored_then_grok_fallback(self):
        self.assertIn(
            'ApplyAuthoredWorldMapsWithGrokFallback(_plaster, "lime_plaster", "plaster_wall", 1.5f, 1.5f, true);',
            self.source,
        )

    def test_authored_manifest_and_full_size_albedo_gate_authored_priority(self):
        body = extract_method_body(
            self.source,
            "private static void ApplyAuthoredWorldMapsWithGrokFallback(",
        )
        self.assertIn("WorldMaterialSourcePolicy.IsAuthoredAvailable(", body)
        self.assertIn("authoredAlbedo != null && authoredAlbedo.width >= 1024", body)
        self.assertIn(
            "WorldMaterialSourcePolicy.Select(hasAuthoredAlbedo, grokAlbedo != null)",
            body,
        )

    def test_grok_is_used_when_authored_pack_is_not_available(self):
        body = extract_method_body(
            self.source,
            "private static void ApplyAuthoredWorldMapsWithGrokFallback(",
        )
        self.assertIn("if (source == WorldMaterialSource.Grok)", body)
        self.assertIn("ApplyGrokWorldMaps(material, grokKey, tilingX, tilingY);", body)
        self.assertIn(
            "ApplyGeneratedWorldMaps(material, materialKey, tilingX, tilingY, smoothnessInAlbedoAlpha);",
            body,
        )

    def test_grok_loader_reads_matching_albedo_and_normal_resources(self):
        body = extract_method_body(
            self.source,
            "private static void ApplyGrokWorldMaps(",
        )
        self.assertIn('"WorldMaterials/Textures/Grok/" + textureKey', body)
        self.assertIn('Resources.Load<Texture2D>(prefix + "_n")', body)


if __name__ == "__main__":
    unittest.main()
