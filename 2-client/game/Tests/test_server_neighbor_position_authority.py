from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
REGISTRY = ROOT / "Assets/_Project/Scripts/Presentation/EntityViewRegistry.cs"


class ServerNeighborPositionAuthorityChecks(unittest.TestCase):
    def test_neighbor_without_server_coordinates_is_skipped(self):
        source = REGISTRY.read_text(encoding="utf-8")
        self.assertIn("if (neighbor.Position == null) continue;", source)
        self.assertIn("position = ToUnityPosition(neighbor.Position);", source)
        self.assertNotIn("Mathf.Cos(angle) * 2.65f", source)
        self.assertNotIn("Mathf.Sin(angle) * 2.65f", source)

    def test_neighbor_becomes_active_only_after_position_resolution(self):
        source = REGISTRY.read_text(encoding="utf-8")
        start = source.index("var neighbors = frame.Place?.Neighbors;")
        end = source.index("ApplyTalkPresentation(frame, player);", start)
        block = source[start:end]
        resolved = block.index("if (!hasGlobalPosition)")
        skip = block.index("if (neighbor.Position == null) continue;")
        active = block.index("active.Add(id);")
        last_seen = block.index("_neighborLastSeenAt[id] = Time.unscaledTime;")
        create_view = block.index("GetOrCreate(id, neighbor.Name")
        self.assertLess(resolved, skip)
        self.assertLess(skip, active)
        self.assertLess(active, last_seen)
        self.assertLess(last_seen, create_view)


if __name__ == "__main__":
    unittest.main()
