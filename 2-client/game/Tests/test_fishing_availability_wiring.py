from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[3]
SCRIPTS = ROOT / "2-client/game/Assets/_Project/Scripts"


class FishingAvailabilityWiringTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.core = (SCRIPTS / "Runtime/CoreGameplay.cs").read_text(encoding="utf-8")
        cls.controller = (SCRIPTS / "Runtime/VerticalSliceController.cs").read_text(encoding="utf-8")

    def test_availability_comes_from_server_site_flag(self):
        self.assertIn('public bool CanFishHere => Flag(Obj(_place, "site"), "can_fish");', self.core)

    def test_core_gameplay_does_not_offer_fishing_from_terrain_feature_alone(self):
        self.assertIn('if (CanFishHere)', self.core)
        self.assertIn('Рыба здесь сейчас не ловится.', self.core)
        self.assertNotIn('HasFeature("river") && Near("river", 10f) && GUILayout.Button("Закинуть удочку")', self.core)

    def test_fishing_minigame_requires_server_availability(self):
        self.assertIn(': _core.CanFishHere;', self.controller)
        self.assertIn('_activityMiniGame == ActivityMiniGame.Fishing && _core != null && !_core.CanFishHere', self.controller)
        self.assertIn('Рыба здесь сейчас не ловится.', self.controller)


if __name__ == "__main__":
    unittest.main()
