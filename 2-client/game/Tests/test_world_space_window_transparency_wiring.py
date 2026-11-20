import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
DTO = ROOT / "Assets/_Project/Scripts/Networking/Protocol/WorldSpatialDescriptors.cs"
BUILDER = ROOT / "Assets/_Project/Scripts/Presentation/WorldSpacePresentationBuilder.cs"


class WorldSpaceWindowTransparencyWiringTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dto = DTO.read_text(encoding="utf-8")
        cls.builder = BUILDER.read_text(encoding="utf-8")

    def test_surface_dto_reads_optional_transparency(self):
        self.assertIn('[JsonProperty("transparent")] public bool Transparent { get; set; }', self.dto)

    def test_server_window_selects_glass_only_when_explicitly_transparent(self):
        self.assertIn("WorldSpaceSurfacePresentationPolicy.UsesWindowGlass(", self.builder)
        self.assertIn("surface.Kind, surface.Transparent", self.builder)
        self.assertIn("GetWindowGlassMaterial()", self.builder)
        self.assertIn("UsesOpaqueWindowFallback(surface.Kind, surface.Transparent)", self.builder)

    def test_transparent_window_does_not_cast_or_receive_shadows(self):
        self.assertIn("WorldSpaceSurfacePresentationPolicy.CastsShadows(", self.builder)
        self.assertIn("WorldSpaceSurfacePresentationPolicy.ReceivesShadows(", self.builder)

    def test_transparent_material_uses_urp_alpha_blending(self):
        self.assertIn('renderQueue = (int)RenderQueue.Transparent', self.builder)
        self.assertIn('material.SetFloat("_Surface", 1f)', self.builder)
        self.assertIn('material.SetFloat("_ZWrite", 0f)', self.builder)
        self.assertIn('material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT")', self.builder)
        self.assertIn('new Color(0.48f, 0.66f, 0.72f, 0.22f)', self.builder)

    def test_transparent_material_uses_alpha_blend_factors_and_clears_conflicting_modes(self):
        self.assertIn('material.SetFloat("_Blend", 0f)', self.builder)
        self.assertIn('material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha)', self.builder)
        self.assertIn('material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha)', self.builder)
        self.assertIn('material.SetOverrideTag("RenderType", "Transparent")', self.builder)
        self.assertIn('material.DisableKeyword("_ALPHATEST_ON")', self.builder)
        self.assertIn('material.DisableKeyword("_ALPHAPREMULTIPLY_ON")', self.builder)

    def test_transparency_does_not_remove_server_collider(self):
        self.assertIn("if (surface.Collidable)", self.builder)
        self.assertIn("go.AddComponent<MeshCollider>()", self.builder)
        self.assertIn("collider.sharedMesh = mesh;", self.builder)


if __name__ == "__main__":
    unittest.main()
