from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
TARGET_BUILDER = ROOT / "Assets/_Project/Scripts/Presentation/WorldLayoutPoiTargetBuilder.cs"
CONTROLLER = ROOT / "Assets/_Project/Scripts/Runtime/VerticalSliceController.cs"


class TavernCounterInteractionSourceChecks(unittest.TestCase):
    def test_server_tavern_footprint_gets_an_interior_counter_target(self):
        source = TARGET_BUILDER.read_text(encoding="utf-8")
        self.assertIn("BuildTavernCounterTarget(root, layout);", source)
        self.assertIn('building.BuildingId, "tavern"', source)
        self.assertIn('(building.X0 + building.X1) * 0.5d', source)
        self.assertIn('(building.Y0 + building.Y1) * 0.5d', source)
        self.assertIn('"tavern_counter"', source)
        self.assertIn('hitArea.isTrigger = true;', source)

    def test_counter_target_opens_tavern_panel_without_toggling_it_closed(self):
        source = CONTROLLER.read_text(encoding="utf-8")
        self.assertIn('id == "tavern_counter"', source)
        self.assertIn('var open = isCounterTarget || !_tavernPanelOpen;', source)
        self.assertIn("Подойдите к стойке внутри корчмы, чтобы заказать пиво.", source)


if __name__ == "__main__":
    unittest.main()
