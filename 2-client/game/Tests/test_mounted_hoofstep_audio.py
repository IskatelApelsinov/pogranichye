from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = ROOT / "Assets/_Project/Scripts"


class MountedHoofstepAudioTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audio = (SCRIPTS / "Presentation/WorldAudioDirector.cs").read_text(encoding="utf-8")
        cls.controller = (SCRIPTS / "Runtime/VerticalSliceController.cs").read_text(encoding="utf-8")

    def test_hoofstep_clips_are_created_and_released(self):
        self.assertIn('_hoofWalk = MakeHoofstep(', self.audio)
        self.assertIn('_hoofRun = MakeHoofstep(', self.audio)
        self.assertIn('DestroyClip(_hoofWalk); DestroyClip(_hoofRun);', self.audio)

    def test_hoofsteps_have_separate_walk_and_trot_cadence(self):
        self.assertIn('running ? 0.34f : 0.58f', self.audio)
        self.assertIn('running ? _hoofRun : _hoofWalk', self.audio)

    def test_confirmed_mounted_movement_selects_hoofsteps(self):
        self.assertIn('WorldAudioEventPolicy.ShouldPlayHoofstep(inputMoveDistanceSquared, isMounted)', self.controller)
        self.assertIn('_audioDirector?.Hoofsteps(canRun);', self.controller)
        self.assertIn('else if (WorldAudioEventPolicy.ShouldPlayFootstep(inputMoveDistanceSquared, isMounted))', self.controller)


if __name__ == "__main__":
    unittest.main()
