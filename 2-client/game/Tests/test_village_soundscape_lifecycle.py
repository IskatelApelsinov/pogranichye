from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/_Project/Scripts/Presentation/VillageSoundscape.cs"


class VillageSoundscapeLifecycleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")

    def test_disable_stops_every_looping_source(self):
        disable = self.source.split("private void OnDisable()", 1)[1].split(
            "private void StartAmbientLoops()", 1
        )[0]
        for source in ("_wind", "_birds", "_water", "_forge", "_hearth", "_tavern", "_nightInsects"):
            self.assertIn(f"StopSource({source});", disable)

    def test_enable_restarts_ambient_and_only_available_server_anchors(self):
        enable = self.source.split("private void OnEnable()", 1)[1].split(
            "private void OnDisable()", 1
        )[0]
        self.assertIn("StartAmbientLoops();", enable)
        self.assertIn("StartAvailableAnchorLoops();", enable)
        anchors = self.source.split("private void StartAvailableAnchorLoops()", 1)[1].split(
            "private static void PlaySource", 1
        )[0]
        for flag, source in (
            ("_waterAvailable", "_water"),
            ("_forgeAvailable", "_forge"),
            ("_hearthAvailable", "_hearth"),
            ("_tavernAvailable", "_tavern"),
        ):
            self.assertIn(f"if ({flag}) PlaySource({source});", anchors)

    def test_server_anchor_availability_is_retained(self):
        for kind, flag in (
            ('"water"', "_waterAvailable"),
            ('"forge"', "_forgeAvailable"),
            ('"hearth"', "_hearthAvailable"),
            ('"tavern"', "_tavernAvailable"),
        ):
            self.assertIn(f"case {kind}:", self.source)
            self.assertIn(f"{flag} = available;", self.source)

    def test_anchor_does_not_start_while_component_is_disabled(self):
        self.assertIn("if (isActiveAndEnabled) PlaySource(source);", self.source)

    def test_destroy_stops_sources_before_releasing_generated_clips(self):
        destroy = self.source.split("private static void DestroySource", 1)[1]
        self.assertIn("source.Stop();", destroy)
        self.assertIn("if (clip != null) Destroy(clip);", destroy)


if __name__ == "__main__":
    unittest.main()
