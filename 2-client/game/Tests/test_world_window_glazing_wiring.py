import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]
BUILDER = ROOT / "Assets/_Project/Scripts/Presentation/WorldLayoutPresentationBuilder.cs"


class WorldWindowGlazingWiringTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = BUILDER.read_text(encoding="utf-8")

    def test_window_glass_material_is_translucent_and_transparent_queued(self):
        self.assertIn("private static Material _windowGlass;", self.source)
        self.assertIn("MakeWindowGlassMaterial()", self.source)
        self.assertIn("renderQueue = (int)RenderQueue.Transparent", self.source)
        self.assertIn('new Color(0.48f, 0.66f, 0.72f, 0.22f)', self.source)
        self.assertIn('material.SetFloat("_ZWrite", 0f)', self.source)

    def test_each_server_window_aperture_gets_a_glass_pane(self):
        self.assertIn('Create("Window glass pane", PrimitiveType.Cube', self.source)
        self.assertIn("WorldWindowAperturePolicy.ResolveWindowBottom(floor)", self.source)
        self.assertIn("WorldWindowAperturePolicy.ResolveWindowTop(floor)", self.source)

    def test_glass_does_not_cast_or_receive_shadows(self):
        self.assertIn("paneRenderer.shadowCastingMode = ShadowCastingMode.Off;", self.source)
        self.assertIn("paneRenderer.receiveShadows = false;", self.source)

    def test_glass_material_is_owned_by_layout_lifecycle(self):
        self.assertIn("_activeBuildAssets.Add(_windowGlass);", self.source)
        self.assertIn("_windowGlass = null;", self.source)


if __name__ == "__main__":
    unittest.main()
