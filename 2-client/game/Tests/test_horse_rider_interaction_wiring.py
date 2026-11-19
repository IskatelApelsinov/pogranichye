
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


from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[3]
SCRIPTS = ROOT / "2-client/game/Assets/_Project/Scripts"


class HorseRiderInteractionWiringTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.core = (SCRIPTS / "Runtime/CoreGameplay.cs").read_text(encoding="utf-8")
        cls.input = (SCRIPTS / "Runtime/ClientInput.cs").read_text(encoding="utf-8")
        cls.camera = (SCRIPTS / "Presentation/PresentationCameraRig.cs").read_text(encoding="utf-8")
        cls.probe = (SCRIPTS / "Presentation/WorldInteractionProbe.cs").read_text(encoding="utf-8")

    def test_rider_pose_is_above_saddle(self):
        self.assertIn('_riderBody.localPosition = new Vector3(0f, 2.25f, -0.02f);', self.core)
        self.assertIn('_riderHead.localPosition = new Vector3(0f, 2.92f, 0.02f);', self.core)
        self.assertIn('_riderVisualRoot.localPosition = Vector3.zero;', self.core)

    def test_mounted_first_person_camera_clears_horse_body(self):
        self.assertIn('var eyeHeight = _mounted ? 2.55f : 1.62f;', self.camera)
        self.assertIn('public void SetMounted(bool mounted)', self.camera)
        self.assertIn('(_mounted ? 9f : 0f)', self.camera)
        self.assertIn('if (_rig != null) _rig.SetMounted(true);', self.core)
        self.assertIn('if (_rig != null) _rig.SetMounted(false);', self.core)

    def test_hold_e_requests_server_dismount(self):
        self.assertIn('public bool InteractReleased => _interact.WasReleasedThisFrame();', self.input)
        self.assertIn('heldSeconds >= 0.45f', self.core)
        self.assertIn('Ask("Спешиться", "horse", "action", "dismount", "target", _horse.Id);', self.core)

    def test_mounted_e_does_not_trigger_poi_interaction(self):
        self.assertIn('public bool ConsumesWorldInteraction => _horse != null && _horse.Mounted;', self.core)
        self.assertIn('!_core.ConsumesWorldInteraction', self.probe)


    def test_interrupted_hold_is_cancelled_on_modal_focus_and_journey_transitions(self):
        for signature in (
            "public void SetExternalCharacterMenuOpen(bool open)",
            "public void SetExternalUiCapture(bool captured)",
            "public void SetExternalModalMenuOpen(bool open)",
        ):
            self.assertIn("CancelHorseInteractionInput();", extract_method_body(self.core, signature))

        update_body = extract_method_body(self.core, "private void Update()")
        self.assertGreaterEqual(update_body.count("CancelHorseInteractionInput();"), 3)

    def test_cancel_resets_pending_flag_and_hold_timestamp(self):
        cancel_body = extract_method_body(self.core, "private void CancelHorseInteractionInput()")
        self.assertIn("_horseInteractPending = false;", cancel_body)
        self.assertIn("_horseInteractStartedAt = 0f;", cancel_body)

if __name__ == "__main__":
    unittest.main()
