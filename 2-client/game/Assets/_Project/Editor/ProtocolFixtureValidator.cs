#if UNITY_EDITOR
using System;
using System.IO;
using Pogranichye.Client.Networking;
using Pogranichye.Client.Presentation;
using UnityEditor;
using UnityEngine;

namespace Pogranichye.Client.Editor
{
    public static class ProtocolFixtureValidator
    {
        [MenuItem("Tools/Pogranichye/Validate shared v1 fixture")]
        public static void Validate()
        {
            var fixturePath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../../3-dev/contract/fixtures/state_v1.example.json"));

            if (!File.Exists(fixturePath))
            {
                Debug.LogError("Shared v1 fixture not found. Expected: " + fixturePath);
                return;
            }

            try
            {
                var frame = WorldFrameParser.Parse(File.ReadAllText(fixturePath));
                if (!frame.Alive || frame.Me == null || frame.Place == null)
                    throw new FormatException("The shared example is expected to be a living frame with place data.");

                if (frame.Me.Position == null || frame.Me.Position.Sequence != 0)
                    throw new FormatException("The shared v1.1 fixture must include me.pos with sequence 0.");
                if (frame.Place.Neighbors == null || frame.Place.Neighbors.Count == 0 || frame.Place.Neighbors[0].Position == null)
                    throw new FormatException("The shared v1.1 fixture must include a positioned neighbor.");
                if (frame.Me.Inventory == null || frame.Me.Inventory.Items == null || frame.Me.World == null ||
                    !frame.Me.Inventory.Items.TryGetValue("axe", out var fixtureAxeCount) || fixtureAxeCount != 1 ||
                    frame.Me.Inventory.Hand != "axe")
                    throw new FormatException("The shared v1.7 fixture must map carried item counts and hand equipment.");
                if (frame.Me.Stash == null || !frame.Me.Stash.TryGetValue("bread", out var fixtureBreadCount) ||
                    fixtureBreadCount != 4)
                    throw new FormatException("The shared v1 fixture must keep bulk goods in me.stash, separate from me.inv.items.");
                if (frame.Me.Desire == null || frame.Place.BeerPrice <= 0)
                    throw new FormatException("The shared v1.7 fixture must include desire and tavern price data.");
                if (frame.Me.Task == null || frame.Me.Task.Stage < 1 || string.IsNullOrEmpty(frame.Me.Task.Text))
                    throw new FormatException("The shared v1.8 fixture must include the server-authoritative opening task.");
                if (frame.Place.Neighbors[0].Gear == null || frame.Place.Neighbors[0].Gear.Worn == null)
                    throw new FormatException("The shared v1.8 fixture must include the public neighbor gear subset.");
                if (frame.Place.Neighbors[0].Controlled || string.IsNullOrEmpty(frame.Place.Neighbors[0].Want))
                    throw new FormatException("The shared fixture must map neighbors[].player and neighbors[].want.");
                if (frame.Place.Neighbors[0].Trait == null || frame.Me.Skills == null || frame.Me.SocialEvent == null || frame.Me.SocialEvent.Sequence != 7)
                    throw new FormatException("The shared v1.6 fixture must include skills and neighbor traits.");
                if (frame.Me.WorldPosition == null || !frame.Me.WorldPosition.IsValid ||
                    frame.Place.Anchor == null || frame.Place.Anchor.Frame != frame.Me.WorldPosition.Frame)
                    throw new FormatException("The shared v1.19 fixture must include valid global me.wpos and place.anchor.");
                if (frame.Me.Position == null ||
                    Math.Abs(frame.Me.WorldPosition.East - frame.Place.Anchor.East - frame.Me.Position.X) > 0.002d ||
                    Math.Abs(frame.Me.WorldPosition.North - frame.Place.Anchor.North - frame.Me.Position.Y) > 0.002d)
                    throw new FormatException("v1.19 global position must equal settlement anchor plus local scene position (2 mm tolerance).");
                if (frame.Me.Weave == null || frame.Me.Weave.Known == null ||
                    frame.Me.Weave.Nodes == null || frame.Me.Weave.MaxLength <= 0)
                    throw new FormatException("The shared v1 fixture must include the weave catalogue and known schemas.");
                if (frame.Place.Life == null || frame.Place.Life.Population < 0 || frame.Place.Life.Working < 0 ||
                    frame.Place.Life.Activities == null || frame.Place.Life.Stock == null || frame.Place.Life.Activities.Maintenance < 0 ||
                    frame.Place.Life.Stock.Wood < 0 || frame.Place.Life.Stock.Metal < 0 || frame.Place.Beer < 0)
                    throw new FormatException("The shared v1.18 fixture must include a valid settlement life summary.");

                ValidateNegativeCases();
                ValidateJournalCases();
                ValidateMapProjection();
                ValidateWorldPositionProjection();
                ValidateWorldV119Dtos();
                ValidateWorldV120Dtos();
                Debug.Log($"v1.20 fixture valid. Player ID: {frame.Me.Id}; seq: {frame.Me.Position.Sequence}; neighbors: {frame.Place.Neighbors?.Count ?? 0}; settlement population: {frame.Place.Life.Population}. Strict parser and map projection checks passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("v1 fixture validation failed: " + ex);
            }
        }

        private static void ValidateNegativeCases()
        {
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":42}}", "numeric me.id");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"},\"place\":{\"neighbors\":[{\"id\":43}]}}", "numeric neighbor.id");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"},\"place\":{\"neighbors\":{}}}", "non-array neighbors");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"pos\":{\"x\":0,\"y\":0}}}", "incomplete me.pos");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"pos\":{\"x\":31,\"y\":0,\"facing\":90,\"stam\":1.0,\"seq\":0}}}", "out-of-bounds scene position");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"pos\":{\"x\":0,\"y\":0,\"facing\":90,\"stam\":1.0,\"seq\":\"1\"}}}", "string movement sequence");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"pos\":{\"x\":0,\"y\":0,\"facing\":90,\"stam\":1.0,\"seq\":9223372036854775808}}}", "overflowing movement sequence");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"18446744073709551616\"}}", "overflowing me.id");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":257,\"n\":-1,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[0,-1],\"layer\":1}}}", "global chunk and layer inconsistent with coordinates");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":1,\"n\":1,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[0,0],\"layer\":1}},\"place\":{\"anchor\":{\"frame\":1,\"e\":0,\"n\":0,\"u\":120}}}", "settlement anchor frame mismatch");
            ExpectRejected("{\"v\":1,\"me\":{\"id\":\"42\"}}", "missing alive");
            ExpectRejected("{\"v\":\"1\",\"alive\":false}", "string version");
            ExpectRejected("{\"v\":2,\"alive\":false}", "unsupported version");

            var legacy = WorldFrameParser.Parse("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"}}");
            if (legacy.Me == null || legacy.Me.Position != null)
                throw new FormatException("A legacy v1.0 frame without pos must remain parseable.");

            var terminal = WorldFrameParser.Parse("{\"v\":1,\"alive\":false,\"msg\":\"left\"}");
            if (terminal.Alive || terminal.Me != null)
                throw new FormatException("alive:false terminal frame must not require me.");
        }

        private static void ValidateJournalCases()
        {
            var withJournal = WorldFrameParser.Parse(
                "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"journal\":[{\"seq\":1,\"day\":40,\"kind\":\"met\",\"text\":\"Познакомился с жителем деревни.\"}]}}");
            if (withJournal.Me.Journal == null || withJournal.Me.Journal.Count != 1 ||
                withJournal.Me.Journal[0].Sequence != 1 || withJournal.Me.Journal[0].Day != 40 ||
                withJournal.Me.Journal[0].Kind != "met")
                throw new FormatException("Optional me.journal entry was not deserialized correctly.");

            var nullJournal = WorldFrameParser.Parse(
                "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"journal\":null}}");
            if (nullJournal.Me.Journal != null)
                throw new FormatException("A null optional me.journal must remain compatible.");

            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"journal\":{}}}", "non-array journal");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"journal\":[{\"seq\":1,\"day\":40,\"kind\":\"met\",\"text\":\"A\"},{\"seq\":1,\"day\":41,\"kind\":\"worked\",\"text\":\"B\"}]}}", "non-monotonic journal sequence");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"journal\":[{\"seq\":1,\"day\":40,\"kind\":\"met\",\"text\":\" \"}]}}", "empty journal text");
        }

        private static void ValidateWorldV119Dtos()
        {
            const string manifestJson = "{\"scheme\":\"yof-tangent\",\"v\":1,\"units\":\"m\",\"chunk_m\":256,\"layer_m\":64,\"seamless\":false,\"frames\":[{\"id\":0,\"projection\":\"azimuthal-equidistant\",\"origin_lat\":0,\"origin_lon\":0,\"radius_m\":1000000}],\"rim_radius_m\":12000,\"settlements\":[{\"id\":\"7\",\"frame\":0,\"e\":256,\"n\":-256,\"u\":120,\"tier\":\"village\",\"terrain\":\"river\",\"plan\":\"cross\",\"cauldron\":true,\"half\":150,\"layout\":\"/api/layout?settlement=7\"}],\"roads\":[{\"a\":\"7\",\"b\":\"8\"}]}";
            var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<Pogranichye.Client.Networking.Protocol.WorldManifestFrame>(manifestJson);
            if (manifest == null || manifest.Version != 1 || manifest.ChunkMeters != 256 ||
                manifest.Settlements == null || manifest.Settlements.Count != 1 ||
                manifest.Settlements[0].Id != "7" || manifest.Settlements[0].East != 256d)
                throw new FormatException("World manifest v1.19 DTO deserialization failed.");

            const string terrainJson = "{\"id\":\"0:1:-1\",\"frame\":0,\"chunk\":[1,-1],\"origin\":{\"e\":256,\"n\":-256},\"size_m\":256,\"step_m\":64,\"side\":5,\"heights\":[120,120,120,120,120,120,120,120,120,120,120,120,120,120,120,120,120,120,120,120,120,120,120,120,120],\"settlements\":[]}";
            var terrain = Newtonsoft.Json.JsonConvert.DeserializeObject<Pogranichye.Client.Networking.Protocol.TerrainChunkFrame>(terrainJson);
            if (terrain == null || !terrain.IsValid || terrain.Chunk[0] != 1 || terrain.Chunk[1] != -1)
                throw new FormatException("Terrain chunk v1.19 DTO deserialization failed.");

            const string layoutJson = "{\"version\":2,\"world_half\":150,\"village_half\":50,\"buildings\":[{\"name\":\"Tavern\",\"kind\":\"tavern\",\"x0\":10,\"y0\":6,\"x1\":24,\"y1\":18}],\"river\":{\"y0\":-190,\"y1\":-180,\"bridge_x0\":-6,\"bridge_x1\":6},\"pois\":{\"tavern\":{\"x\":17,\"y\":4},\"well\":{\"x\":0,\"y\":0},\"woods\":{\"x\":80,\"y\":10},\"garden\":{\"x\":0,\"y\":-80},\"river\":{\"x\":20,\"y\":70},\"mine\":{\"x\":-90,\"y\":-90}},\"plan\":\"cross\",\"has_river\":false,\"zones\":[{\"kind\":\"forest\",\"x0\":-120,\"y0\":-100,\"x1\":-40,\"y1\":100}],\"roads\":[[0,0,20,10]],\"homes\":[{\"x\":-10,\"y\":12}],\"entry\":{\"x\":0,\"y\":-120},\"open\":false,\"settlement\":\"7\",\"anchor\":{\"frame\":0,\"e\":256,\"n\":-256,\"u\":120}}";
            var layout = Newtonsoft.Json.JsonConvert.DeserializeObject<Pogranichye.Client.Networking.Protocol.WorldLayoutFrame>(layoutJson);
            if (layout == null || layout.Plan != "cross" || layout.HasRiver != false ||
                layout.Settlement != "7" || layout.Anchor == null || layout.Anchor.Frame != 0 ||
                layout.Zones == null || layout.Zones.Count != 1 || layout.Roads == null ||
                layout.Roads.Count != 1 || layout.Homes == null || layout.Homes.Count != 1 ||
                layout.Entry == null || layout.Entry.X != 0d || !layout.IsGenerated)
                throw new FormatException("Settlement layout v1.19 DTO deserialization failed.");
        }

        private static void ValidateWorldPositionProjection()
        {
            var origin = new Pogranichye.Client.Networking.Protocol.WorldPositionFrame
            {
                Scheme = "yof-tangent", Version = 1, Frame = 0,
                East = 102d, North = 204d, Up = 120d, Ground = 120d,
                HeightAboveGround = 0d, Chunk = new long[] { 0, 0 }, Layer = 1
            };
            var neighbor = new Pogranichye.Client.Networking.Protocol.WorldPositionFrame
            {
                Scheme = "yof-tangent", Version = 1, Frame = 0,
                East = 105d, North = 208d, Up = 122d, Ground = 122d,
                HeightAboveGround = 0d, Chunk = new long[] { 0, 0 }, Layer = 1
            };
            var localOrigin = new Pogranichye.Client.Networking.Protocol.ScenePositionFrame { X = 2d, Y = 4d };
            if (!WorldPositionProjection.TryToUnityPosition(neighbor, origin, localOrigin, out var projected))
                throw new FormatException("Valid global neighbor position should project into the player's local frame.");
            if (Mathf.Abs(projected.x - 5f) > 0.001f ||
                Mathf.Abs(projected.y - 2f) > 0.001f ||
                Mathf.Abs(projected.z - 8f) > 0.001f)
                throw new FormatException("Global neighbor projection must preserve east/north offsets and relative elevation.");

            neighbor.Frame = 1;
            if (WorldPositionProjection.TryToUnityPosition(neighbor, origin, localOrigin, out _))
                throw new FormatException("Global positions from different frames must not be mixed.");
        }

        private static void ValidateWorldV120Dtos()
        {
            const string frameJson = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"room\":{\"id\":\"9001\",\"settlement\":\"7\",\"door_poi\":\"tavern\",\"rent_price\":6,\"paid_through_day\":40,\"grace_until_day\":41,\"lease_state\":\"active\",\"indoor\":true,\"chest_cap\":40,\"chest_load\":1,\"chest_items\":{\"torch\":1}},\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":1,\"n\":1,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[0,0],\"layer\":1}},\"place\":{\"anchor\":{\"frame\":0,\"e\":0,\"n\":0,\"u\":120},\"rooms\":{\"total\":2,\"vacant\":1,\"rent_price\":6,\"items\":[{\"id\":\"9001\",\"lease_state\":\"vacant\",\"rent_price\":6},{\"id\":\"9002\",\"lease_state\":\"active\",\"rent_price\":6}]},\"neighbors\":[{\"id\":\"43\",\"pos\":{\"x\":2,\"y\":3,\"facing\":90},\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":2,\"n\":3,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[0,0],\"layer\":1}}]}}";
            var frame = WorldFrameParser.Parse(frameJson);
            if (frame.Me.Room == null || frame.Me.Room.Id != "9001" ||
                frame.Me.Room.LeaseState != "active" || !frame.Me.Room.Indoor ||
                frame.Me.Room.ChestItems == null || frame.Me.Room.ChestItems["torch"] != 1)
                throw new FormatException("v1.20 me.room DTO deserialization failed.");
            if (frame.Place.Rooms == null || frame.Place.Rooms.Total != 2 ||
                frame.Place.Rooms.Vacant != 1 || frame.Place.Rooms.RentPrice != 6d ||
                frame.Place.Rooms.Items == null || frame.Place.Rooms.Items.Count != 2 ||
                frame.Place.Rooms.Items[0].Id != "9001" || frame.Place.Rooms.Items[0].LeaseState != "vacant")
                throw new FormatException("v1.20 place.rooms DTO deserialization failed.");
            if (frame.Place.Neighbors == null || frame.Place.Neighbors.Count != 1 ||
                frame.Place.Neighbors[0].WorldPosition == null || !frame.Place.Neighbors[0].WorldPosition.IsValid)
                throw new FormatException("v1.20 neighbors[].wpos DTO deserialization failed.");

            var legacy = WorldFrameParser.Parse("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"},\"place\":{\"id\":\"7\"}}");
            if (legacy.Me.Room != null || legacy.Place.Rooms != null)
                throw new FormatException("v1.0-v1.19 frames without v1.20 room fields must remain compatible.");

            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"},\"place\":{\"anchor\":{\"frame\":0,\"e\":0,\"n\":0,\"u\":0},\"neighbors\":[{\"id\":\"43\",\"pos\":{\"x\":2,\"y\":3,\"facing\":90},\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":300,\"n\":3,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[1,0],\"layer\":1}}]}}", "neighbor wpos inconsistent with local pos");
        }

        private static void ValidateMapProjection()
        {
            var map = new Rect(10f, 20f, 300f, 300f);
            AssertMapPoint(WorldMapProjection.ToMapPoint(-150d, -150d, 150d, map),
                new Vector2(10f, 320f), "south-west map corner");
            AssertMapPoint(WorldMapProjection.ToMapPoint(150d, 150d, 150d, map),
                new Vector2(310f, 20f), "north-east map corner");
            AssertMapPoint(WorldMapProjection.ToMapPoint(0d, 0d, 150d, map),
                new Vector2(160f, 170f), "map centre");
            AssertMapPoint(WorldMapProjection.ToMapPoint(300d, 0d, 150d, map),
                new Vector2(460f, 170f), "off-screen map point remains unclamped for viewport clipping");

            var footprint = WorldMapProjection.ToMapRect(-10d, -20d, 10d, 20d, 150d, map);
            if (Mathf.Abs(footprint.x - 150f) > 0.01f ||
                Mathf.Abs(footprint.y - 150f) > 0.01f ||
                Mathf.Abs(footprint.width - 20f) > 0.01f ||
                Mathf.Abs(footprint.height - 40f) > 0.01f)
                throw new FormatException("Map footprint projection must preserve dimensions and north-up orientation.");

            try
            {
                WorldMapProjection.ToMapPoint(0d, 0d, 0d, map);
            }
            catch (ArgumentOutOfRangeException) { return; }

            throw new FormatException("Map projection must reject a non-positive world extent.");
        }

        private static void AssertMapPoint(Vector2 actual, Vector2 expected, string caseName)
        {
            if (Mathf.Abs(actual.x - expected.x) > 0.01f || Mathf.Abs(actual.y - expected.y) > 0.01f)
                throw new FormatException("Map projection failed " + caseName + ": expected " + expected + ", got " + actual + ".");
        }

        private static void ExpectRejected(string json, string caseName)
        {
            try
            {
                WorldFrameParser.Parse(json);
            }
            catch (FormatException) { return; }
            catch (NotSupportedException) { return; }

            throw new FormatException("Parser unexpectedly accepted invalid case: " + caseName);
        }
    }
}
#endif
