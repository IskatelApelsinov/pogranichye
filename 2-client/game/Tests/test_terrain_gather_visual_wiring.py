from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
HTTP = ROOT / "Assets/_Project/Scripts/Networking/HttpAuthClient.cs"
FRAME = ROOT / "Assets/_Project/Scripts/Networking/Protocol/WorldFrame.cs"
TERRAIN = ROOT / "Assets/_Project/Scripts/Presentation/WorldTerrainChunkBuilder.cs"
BUILDER = ROOT / "Assets/_Project/Scripts/Presentation/WorldGatherNodePresentationBuilder.cs"
VERTICAL = ROOT / "Assets/_Project/Scripts/Runtime/VerticalSliceController.cs"


class TerrainGatherVisualWiringChecks(unittest.TestCase):
    def test_optional_chunk_data_is_normalized_before_dto_deserialization(self):
        source = HTTP.read_text(encoding="utf-8")
        self.assertIn('terrainDocument["gather"] = TerrainGatherNodeFrameParser.Normalize', source)
        self.assertIn("terrainDocument.ToObject<TerrainChunkFrame>()", source)

    def test_chunk_frame_exposes_the_existing_optional_gather_array(self):
        source = FRAME.read_text(encoding="utf-8")
        self.assertIn('[JsonProperty("gather")] public List<TerrainGatherNodeFrame> Gather', source)

    def test_visuals_use_server_coordinates_and_terrain_height_without_colliders(self):
        terrain = TERRAIN.read_text(encoding="utf-8")
        builder = BUILDER.read_text(encoding="utf-8")
        self.assertIn("WorldGatherNodePresentationBuilder.Apply(root, chunk, anchor)", terrain)
        self.assertIn("TerrainHeightSampler.TrySample(chunk, east, north", builder)
        self.assertIn("chunk.Origin.East - anchor.East + node.X", builder)
        self.assertIn("chunk.Origin.North - anchor.North + node.Z", builder)
        self.assertNotIn("AddComponent<Collider>", builder)
        self.assertIn("collider.enabled = false", builder)

    def test_nearby_authoritative_snapshot_refreshes_depleted_and_regrown_nodes(self):
        vertical = VERTICAL.read_text(encoding="utf-8")
        builder = BUILDER.read_text(encoding="utf-8")
        self.assertIn("RefreshGatherNodePresentation(frame)", vertical)
        self.assertIn("WorldGatherNodePresentationBuilder.Refresh", vertical)
        self.assertIn("SetActive(TerrainGatherNodeVisualPolicy.ShouldBeVisible(node.Remaining))", builder)


if __name__ == "__main__":
    unittest.main()
