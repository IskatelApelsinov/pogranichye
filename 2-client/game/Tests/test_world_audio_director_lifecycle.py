import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/_Project/Scripts/Presentation/WorldAudioDirector.cs"


class WorldAudioDirectorLifecycleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SOURCE.read_text(encoding="utf-8")

    def test_event_subscriptions_are_idempotent_and_balanced(self):
        self.assertIn("if (_eventsSubscribed) return;", self.source)
        self.assertIn("_eventsSubscribed = true;", self.source)
        self.assertIn("if (!_eventsSubscribed) return;", self.source)
        self.assertIn("_eventsSubscribed = false;", self.source)
        self.assertIn("EntityMotion.NearbyFootstep -= OnNearbyFootstep;", self.source)
        self.assertIn("EntityMotion.NearbyMagicCast -= OnNearbyMagicCast;", self.source)
        self.assertIn("EntityMotion.NearbyWorkAction -= OnNearbyWorkAction;", self.source)

    def test_disable_unsubscribes_and_stops_all_voices(self):
        disable = self.source.split("private void OnDisable()", 1)[1].split("private void SubscribeEvents()", 1)[0]
        self.assertIn("UnsubscribeEvents();", disable)
        self.assertIn("StopPlayback();", disable)
        self.assertIn("if (_oneShot != null) _oneShot.Stop();", self.source)
        self.assertIn("if (_nearbyVoices[i] != null) _nearbyVoices[i].Stop();", self.source)

    def test_public_sfx_entry_points_respect_component_state(self):
        self.assertIn("if (!isActiveAndEnabled || Time.unscaledTime < _nextStepAt", self.source)
        self.assertIn("if (isActiveAndEnabled && _oneShot != null) _oneShot.PlayOneShot(_magic", self.source)
        self.assertIn("if (isActiveAndEnabled && _oneShot != null) _oneShot.PlayOneShot(_beer", self.source)
        self.assertIn("if (!isActiveAndEnabled || _oneShot == null || clip == null", self.source)


if __name__ == "__main__":
    unittest.main()
