#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Pogranichye.Client;
using Pogranichye.Client.Networking;
using Pogranichye.Client.Networking.Protocol;
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
                if (!frame.Hour.HasValue || frame.Hour.Value < 0 || frame.Hour.Value > 23)
                    throw new FormatException("The shared v1 fixture must deserialize the authoritative hour in range 0..23.");

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
                ValidateSpatialDescriptorDtos();
                ValidateTrailDtos();
                ValidateWorldV120Dtos();
                ValidateClaudeV131V133Dtos();
                ValidateFastFrameCases();
                ValidateFeedCases();
                ValidateActionResultCases();
                ValidateCombatFoeCases();
                ValidateJourneyQuestCases();
                ValidateRegionCases();
                ValidateGameplayStatusView(frame);
                Debug.Log($"v1.20 fixture valid. Player ID: {frame.Me.Id}; seq: {frame.Me.Position.Sequence}; neighbors: {frame.Place.Neighbors?.Count ?? 0}; settlement population: {frame.Place.Life.Population}. Full and fast frame parser checks passed.");
            }
            catch (Exception ex)
            {
                Debug.LogError("v1 fixture validation failed: " + ex);
            }
        }


        private static void ValidateTrailDtos()
        {
            const string json = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"trail\":{\"id\":\"road-2-54\",\"kind\":\"trail\",\"a\":\"Починок\",\"b\":\"Броды\",\"boost\":1.15}},\"place\":{}}";
            var frame = WorldFrameParser.Parse(json);
            if (frame.Me?.Trail == null || frame.Me.Trail.Kind != "trail" ||
                frame.Me.Trail.FromSettlement != "Починок" ||
                Math.Abs(frame.Me.Trail.Boost - 1.15d) > 0.0001d)
                throw new FormatException("v1.27 me.trail must deserialize its kind, settlement names and boost.");

            const string manifestJson = "{\"scheme\":\"yof-tangent\",\"v\":1,\"units\":\"m\",\"chunk_m\":256,\"layer_m\":64,\"movement\":{\"walk_mps\":1.4,\"jog_mps\":3.2,\"run_mps\":6,\"trail\":{\"trail_boost\":1.15,\"road_boost\":1.25,\"trail_width_m\":1.6,\"road_width_m\":4,\"reach_m\":1.5}},\"roads\":[{\"a\":\"2\",\"b\":\"54\",\"id\":\"road-2-54\",\"kind\":\"trail\",\"width_m\":1.6,\"len_m\":1234}]}";
            var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<WorldManifestFrame>(manifestJson);
            if (manifest?.Movement?.Trail == null || manifest.Movement.Trail.TrailBoost != 1.15d ||
                manifest.Movement.Trail.RoadBoost != 1.25d || manifest.Movement.Trail.ReachMeters != 1.5d ||
                manifest.Roads == null || manifest.Roads.Count != 1 ||
                manifest.Roads[0].Kind != "trail" || manifest.Roads[0].WidthMeters != 1.6d ||
                manifest.Roads[0].LengthMeters != 1234d)
                throw new FormatException("v1.27 world movement.trail and world.roads metadata must deserialize.");

            const string terrainJson = "{\"id\":\"0:0:0\",\"frame\":0,\"chunk\":[0,0],\"origin\":{\"e\":0,\"n\":0},\"size_m\":256,\"step_m\":64,\"side\":5,\"heights\":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],\"roads\":[{\"id\":\"road-2-54\",\"kind\":\"trail\",\"width_m\":1.6,\"points\":[{\"x\":1,\"z\":2},{\"x\":30,\"z\":40},{\"x\":80,\"z\":90}]},{\"id\":\"road-2-54#2\",\"kind\":\"trail\",\"width_m\":1.6,\"points\":[{\"x\":240,\"z\":200},{\"x\":250,\"z\":220}]}]}";
            var terrain = Newtonsoft.Json.JsonConvert.DeserializeObject<TerrainChunkFrame>(terrainJson);
            if (terrain?.Roads == null || terrain.Roads.Count != 2 ||
                terrain.Roads[0].Points == null || terrain.Roads[0].Points.Count != 3 ||
                terrain.Roads[1].Id != "road-2-54#2" || terrain.Roads[1].Points.Count != 2)
                throw new FormatException("v1.27 terrain roads must preserve arbitrary polylines and repeated chunk-run IDs.");
        }

        private static void ValidateFastFrameCases()
        {
            const string fastJson = "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"pos\":{\"x\":4,\"y\":5},\"health\":91,\"combat\":{\"stance\":\"balanced\",\"guard\":false,\"sneak\":false,\"feint\":false,\"cd\":0,\"shield\":0,\"light\":0,\"arrows\":0,\"weapon\":\"\",\"kills\":{},\"ko\":0},\"feed\":[]},\"place\":{\"neighbors\":[{\"id\":\"43\",\"pos\":{\"x\":1,\"y\":2}}],\"foes\":[{\"id\":\"wolf-1\",\"kind\":\"wolf\",\"name\":\"Волк\",\"x\":0,\"y\":0,\"facing\":0,\"hp\":20,\"max\":30,\"state\":\"roam\",\"aware\":false,\"slow\":false,\"weak\":false,\"blind\":false,\"stun\":false,\"windup\":0}]}}";
            if (!FastFrameParser.TryParse(fastJson, out var patch) || patch == null ||
                patch.Me == null || patch.Me["health"]?.ToObject<int>() != 91 || patch.Place == null)
                throw new FormatException("Valid partial fast frame was not parsed.");
            var foes = patch.Place["foes"] as Newtonsoft.Json.Linq.JArray;
            if (foes == null || foes.Count != 1 || foes[0]["id"]?.ToObject<string>() != "wolf-1")
                throw new FormatException("Fast-frame foe array was not parsed.");

            if (FastFrameParser.TryParse("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"}}", out _))
                throw new FormatException("A full frame must not be classified as a fast patch.");

            ExpectFastRejected("{\"v\":2,\"fast\":true,\"alive\":true,\"me\":{\"health\":1}}", "unsupported fast protocol version");
            var terminalFast = WorldFrameParser.Parse("{\"v\":1,\"fast\":true,\"alive\":false}", out _);
            if (terminalFast.Alive || terminalFast.Me != null)
                throw new FormatException("Terminal fast envelope must reach the normal alive:false lifecycle.");
            ExpectFastRejected("{\"v\":1,\"fast\":\"true\",\"alive\":true,\"me\":{\"health\":1}}", "non-boolean fast marker");
            ExpectFastRejected("{\"v\":1,\"fast\":true,\"alive\":true,\"me\":[],\"place\":{}}", "non-object fast me");
            ExpectFastRejected("{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"health\":\"high\"}}", "non-numeric fast health");
            ExpectFastRejected("{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"wounds\":[]}}", "non-object fast wounds");
            ExpectFastRejected("{\"v\":1,\"fast\":true,\"alive\":true,\"place\":{\"foes\":{}}}", "non-array fast foes");
            ExpectFastRejected("{\"v\":1,\"fast\":true,\"alive\":true,\"place\":{\"foes\":[{\"id\":42}]}}", "non-string fast foe id");
        }

        private static void ExpectFastRejected(string json, string caseName)
        {
            try
            {
                FastFrameParser.TryParse(json, out _);
            }
            catch (FormatException) { return; }

            throw new FormatException("Fast frame parser unexpectedly accepted invalid case: " + caseName);
        }

        private static void ValidateGameplayStatusView(WorldFrame frame)
        {
            const string drunkJson = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"drunk\":18}}";
            var drunkFrame = WorldFrameParser.Parse(drunkJson);
            if (drunkFrame.Me == null || drunkFrame.Me.Drunk != 18d)
                throw new FormatException("Full-frame me.drunk must deserialize into CharacterFrame.");

            var full = GameplayStatusView.From(
                frame.Me.Health, frame.Me.Energy, frame.Me.Mana, frame.Me.Drunk,
                frame.Me.Position == null ? 0d : frame.Me.Position.Stamina);

            const string partialJson = "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"health\":47,\"pos\":{\"stam\":0.25}}}";
            if (!FastFrameParser.TryParse(partialJson, out var partialPatch))
                throw new FormatException("A valid typed status fast patch was not recognized.");
            var partial = full.Apply(partialPatch.Status);
            if (partial.Health != 47f || partial.Energy != full.Energy || partial.Mana != full.Mana ||
                partial.Drunk != full.Drunk || Math.Abs(partial.Stamina - 0.25f) > 0.0001f)
                throw new FormatException("A partial fast status patch must update only present fields.");

            const string nullJson = "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"health\":null,\"pos\":null}}";
            if (!FastFrameParser.TryParse(nullJson, out var nullPatch))
                throw new FormatException("A fast status patch with explicit nulls was not recognized.");
            var cleared = partial.Apply(nullPatch.Status);
            if (cleared.Health != 0f || cleared.Stamina != 0f ||
                cleared.Energy != partial.Energy || cleared.Mana != partial.Mana || cleared.Drunk != partial.Drunk)
                throw new FormatException("Explicit nulls must clear replaced numeric values without erasing omitted fields.");

            if (Math.Abs(full.Drunk - (float)frame.Me.Drunk) > 0.0001f)
                throw new FormatException("Full-frame me.drunk must remain available to the typed status view.");
        }

        private static void ValidateFeedCases()
        {
            const string fullFeed = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"feed\":[{\"seq\":4,\"kind\":\"hit\",\"text\":\"Попадание\",\"foe\":\"wolf-1\",\"part\":\"head\",\"dmg\":3.5}]}}";
            var frame = WorldFrameParser.Parse(fullFeed);
            if (frame.Me?.Feed == null || frame.Me.Feed.Count != 1 ||
                frame.Me.Feed[0].Sequence != 4 || frame.Me.Feed[0].Foe != "wolf-1" ||
                Math.Abs(frame.Me.Feed[0].Damage - 3.5d) > 0.0001d)
                throw new FormatException("A full frame must expose validated typed feed entries.");

            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"feed\":[{\"seq\":2,\"kind\":\"hit\",\"text\":\"a\"},{\"seq\":2,\"kind\":\"loot\",\"text\":\"b\"}]}}", "duplicate feed sequence");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"feed\":[{\"seq\":1,\"kind\":\"hit\",\"text\":\"a\",\"dmg\":-1}]}}", "negative feed damage");
        }

        private static void ValidateActionResultCases()
        {
            const string fullResult = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"result\":{\"reqid\":\"full-1\",\"ok\":false,\"reason\":\"нужно подойти\"}}}";
            var frame = WorldFrameParser.Parse(fullResult);
            if (frame.Me?.Result == null || frame.Me.Result.RequestId != "full-1" ||
                frame.Me.Result.Ok || frame.Me.Result.Reason != "нужно подойти")
                throw new FormatException("A full frame must expose a validated typed action result.");

            var nullResult = WorldFrameParser.Parse("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"result\":null}}");
            if (nullResult.Me == null || nullResult.Me.Result != null)
                throw new FormatException("An explicit null me.result must remain null.");

            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"result\":{\"reqid\":\"x\",\"ok\":true}}}", "result missing reason");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"result\":{\"reqid\":42,\"ok\":true,\"reason\":\"ok\"}}}", "numeric result reqid");
        }

        private static void ValidateCombatFoeCases()
        {
            const string full = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"combat\":{\"stance\":\"balanced\",\"guard\":false,\"sneak\":false,\"feint\":false,\"cd\":0,\"shield\":0,\"light\":0,\"arrows\":2,\"weapon\":\"sword\",\"kills\":{\"wolf\":1},\"ko\":0}},\"place\":{\"id\":\"7\",\"foes\":[{\"id\":\"wolf-1\",\"kind\":\"wolf\",\"name\":\"Волк\",\"x\":3,\"y\":4,\"facing\":90,\"hp\":20,\"max\":30,\"state\":\"windup\",\"aware\":true,\"slow\":false,\"weak\":false,\"blind\":false,\"stun\":false,\"windup\":0.4}]}}";
            var frame = WorldFrameParser.Parse(full);
            if (frame.Me?.Combat == null || frame.Me.Combat.Weapon != "sword" ||
                frame.Me.Combat.Arrows != 2 || frame.Place?.Foes == null ||
                frame.Place.Foes.Count != 1 || frame.Place.Foes[0].Id != "wolf-1" ||
                frame.Place.Foes[0].State != "windup")
                throw new FormatException("Full frame combat/foe views must deserialize to typed DTOs.");

            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"combat\":{\"stance\":\"balanced\",\"sneak\":false,\"feint\":false,\"cd\":0,\"shield\":0,\"light\":0,\"arrows\":0,\"weapon\":\"\",\"kills\":{},\"ko\":0}}}", "combat missing guard");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"},\"place\":{\"id\":\"7\",\"foes\":[{\"id\":42,\"kind\":\"wolf\",\"name\":\"Волк\",\"x\":0,\"y\":0,\"facing\":0,\"hp\":1,\"max\":1,\"state\":\"roam\",\"aware\":false}]}}", "foe numeric id");
        }

        private static void ValidateJourneyQuestCases()
        {
            const string full = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"journey\":{\"to\":\"settlement-2\",\"to_name\":\"Лесное\",\"progress\":0.45,\"eta\":37,\"scene\":\"Дорога тянется через лес.\",\"note\":\"\",\"follow\":false,\"event\":{\"kind\":\"ambush\",\"foe\":\"bandit\",\"text\":\"На тропе люди с дубьём.\",\"options\":[\"fight\",\"pay\",\"flee\"]},\"via\":[\"Сосновка\"]},\"quests\":{\"offers\":[{\"id\":\"food:1:2\",\"kind\":\"deliver\",\"via\":\"board\",\"giver\":\"10\",\"giver_name\":\"староста\",\"good\":\"fish\",\"qty\":4,\"text\":\"Принеси рыбу.\",\"coins\":6}],\"active\":[{\"id\":\"cull:wolf:1:2\",\"kind\":\"cull\",\"via\":\"board\",\"giver\":\"10\",\"giver_name\":\"староста\",\"species\":\"wolf\",\"qty\":2,\"text\":\"Убей двух волков.\",\"coins\":8,\"have\":1,\"ready\":false,\"settlement_id\":\"1\",\"settlement_name\":\"Сосновка\",\"realm_name\":\"\"}],\"done\":3,\"rep\":-2}}}";
            var frame = WorldFrameParser.Parse(full);
            if (frame.Me?.Journey == null || frame.Me.Journey.To != "settlement-2" ||
                frame.Me.Journey.Event?.Options == null || frame.Me.Journey.Event.Options.Count != 3 ||
                frame.Me.Quests == null || frame.Me.Quests.Offers.Count != 1 ||
                frame.Me.Quests.Active.Count != 1 || frame.Me.Quests.Active[0].Have != 1 ||
                frame.Me.Quests.Reputation != -2)
                throw new FormatException("Full frame journey/quest views must deserialize to typed DTOs.");

            ExpectRejected(full.Replace("\"progress\":0.45", "\"progress\":1.5"), "journey progress above one");
            ExpectRejected(full.Replace("\"ready\":false", "\"ready\":1"), "quest ready must be boolean");
        }

        private static void ValidateRegionCases()
        {
            const string full = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"region\":{\"nodes\":[{\"id\":\"1\",\"x\":0,\"y\":0,\"state\":\"here\",\"name\":\"Сосновка\",\"tier\":\"village\"},{\"id\":\"2\",\"x\":120,\"y\":-40,\"state\":\"unknown\",\"name\":\"\",\"tier\":\"\"}],\"roads\":[{\"a\":\"1\",\"b\":\"2\",\"len\":120}]}}}";
            var frame = WorldFrameParser.Parse(full);
            if (frame.Me?.Region == null || frame.Me.Region.Nodes.Count != 2 ||
                frame.Me.Region.Nodes[1].State != "unknown" || frame.Me.Region.Nodes[1].Name != "" ||
                frame.Me.Region.Roads.Count != 1)
                throw new FormatException("Full frame regional map must deserialize to typed privacy-safe DTOs.");

            ExpectRejected(full.Replace("\"name\":\"\",\"tier\":\"\"", "\"name\":\"Скрытое место\",\"tier\":\"city\""),
                "unknown regional place name leak");
        }

        private static void ValidateNegativeCases()
        {
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":42}}", "numeric me.id");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"},\"place\":{\"neighbors\":[{\"id\":43}]}}", "numeric neighbor.id");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"},\"place\":{\"neighbors\":{}}}", "non-array neighbors");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"pos\":{\"x\":0,\"y\":0}}}", "incomplete me.pos");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"pos\":{\"x\":151,\"y\":0,\"facing\":90,\"stam\":1.0,\"seq\":0}}}", "out-of-bounds scene position");
            const string extendedScenePosition = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"pos\":{\"x\":151,\"y\":0,\"facing\":90,\"stam\":1,\"seq\":1},\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":151,\"n\":0,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[0,0],\"layer\":1}},\"place\":{\"anchor\":{\"frame\":0,\"e\":0,\"n\":0,\"u\":120}}}";
            var extendedFrame = WorldFrameParser.Parse(extendedScenePosition);
            if (extendedFrame.Me.Position == null || extendedFrame.Me.Position.X != 151d)
                throw new FormatException("A seamless player position outside the settlement footprint must parse when anchor + me.pos matches me.wpos.");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"pos\":{\"x\":151,\"y\":0,\"facing\":90,\"stam\":1,\"seq\":1},\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":152,\"n\":0,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[0,0],\"layer\":1}},\"place\":{\"anchor\":{\"frame\":0,\"e\":0,\"n\":0,\"u\":120}}}", "extended scene position inconsistent with global position");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"pos\":{\"x\":0,\"y\":0,\"facing\":90,\"stam\":1.0,\"seq\":\"1\"}}}", "string movement sequence");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"pos\":{\"x\":0,\"y\":0,\"facing\":90,\"stam\":1.0,\"seq\":9223372036854775808}}}", "overflowing movement sequence");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"18446744073709551616\"}}", "overflowing me.id");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":257,\"n\":-1,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[0,-1],\"layer\":1}}}", "global chunk and layer inconsistent with coordinates");
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":1,\"n\":1,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[0,0],\"layer\":1}},\"place\":{\"anchor\":{\"frame\":1,\"e\":0,\"n\":0,\"u\":120}}}", "settlement anchor frame mismatch");
            ExpectRejected("{\"v\":1,\"me\":{\"id\":\"42\"}}", "missing alive");
            ExpectRejected("{\"v\":\"1\",\"alive\":false}", "string version");
            ExpectRejected("{\"v\":2,\"alive\":false}", "unsupported version");
            ExpectRejected("{\"v\":1,\"alive\":false,\"hour\":24}", "out-of-range world hour");
            ExpectRejected("{\"v\":1,\"alive\":false,\"hour\":\"12\"}", "string world hour");

            var timed = WorldFrameParser.Parse("{\"v\":1,\"alive\":true,\"hour\":23,\"me\":{\"id\":\"42\"}}");
            if (timed.Hour != 23) throw new FormatException("Optional top-level hour was not deserialized.");

            var legacy = WorldFrameParser.Parse("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"}}", out var legacyDocument);
            if (legacy.Me == null || legacy.Me.Position != null || legacy.Hour.HasValue)
                throw new FormatException("A legacy v1.0 frame without pos/hour must remain parseable.");
            if (legacyDocument?["me"]?["hunger"] != null || legacyDocument?["me"]?["pos"] != null)
                throw new FormatException("The validated document must preserve absent optional fields without DTO default expansion.");

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
            const string manifestJson = "{\"scheme\":\"yof-tangent\",\"v\":1,\"units\":\"m\",\"chunk_m\":256,\"layer_m\":64,\"seamless\":false,\"frames\":[{\"id\":0,\"projection\":\"azimuthal-equidistant\",\"origin_lat\":0,\"origin_lon\":0,\"radius_m\":1000000}],\"rim_radius_m\":12000,\"settlements\":[{\"id\":\"7\",\"frame\":0,\"e\":256,\"n\":-256,\"u\":120,\"tier\":\"village\",\"terrain\":\"river\",\"plan\":\"cross\",\"cauldron\":true,\"half\":150,\"layout\":\"/api/layout?settlement=7\"}],\"roads\":[{\"a\":\"7\",\"b\":\"8\"}],\"wildsites\":[{\"id\":\"wild-1\",\"name\":\"Старая башня\",\"wild_type\":\"tower\",\"region\":\"region-1\",\"frame\":0,\"e\":512,\"n\":-128,\"u\":145,\"ground\":120,\"mana\":1.2,\"danger\":0.35}]}";
            var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<Pogranichye.Client.Networking.Protocol.WorldManifestFrame>(manifestJson);
            if (manifest == null || manifest.Version != 1 || manifest.ChunkMeters != 256 ||
                manifest.Settlements == null || manifest.Settlements.Count != 1 ||
                manifest.Settlements[0].Id != "7" || manifest.Settlements[0].East != 256d ||
                manifest.WildSites == null || manifest.WildSites.Count != 1 ||
                manifest.WildSites[0].Id != "wild-1" || manifest.WildSites[0].Type != "tower" ||
                manifest.WildSites[0].East != 512d)
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

        private static void ValidateSpatialDescriptorDtos()
        {
            const string terrainJson = "{\"id\":\"0:0:0\",\"frame\":0,\"chunk\":[0,0],\"origin\":{\"e\":0,\"n\":0},\"size_m\":256,\"step_m\":64,\"side\":5,\"heights\":[0,0,0,0,0,0,0,10,0,0,0,0,20,0,0,0,0,0,0,0,0,0,0,0,0],\"settlements\":[],\"seed\":1234,\"gen\":1,\"biome\":\"mountain\",\"cover\":{\"forest\":0.1,\"grass\":0.2,\"rock\":0.6,\"wet\":0.1},\"water\":[{\"id\":\"river-1\",\"kind\":\"river\",\"width_m\":8,\"water_level_m\":4,\"flow\":0.8,\"points\":[{\"x\":0,\"z\":0,\"surface_m\":4},{\"x\":256,\"z\":256,\"surface_m\":3}]}]}";
            var terrain = Newtonsoft.Json.JsonConvert.DeserializeObject<Pogranichye.Client.Networking.Protocol.TerrainChunkFrame>(terrainJson);
            if (terrain == null || !terrain.IsValid || terrain.Seed != 1234L ||
                terrain.Biome != "mountain" || terrain.Cover == null || terrain.Cover.Rock != 0.6d ||
                terrain.Water == null || terrain.Water.Count != 1 || terrain.Water[0].Points.Count != 2)
                throw new FormatException("Additive terrain descriptors failed to deserialize.");
            if (!Pogranichye.Client.Presentation.TerrainHeightSampler.TrySample(
                    terrain, 128d, 128d, out _, out var slope, out var normal) ||
                slope <= 0f || normal.sqrMagnitude < 0.99f)
                throw new FormatException("Terrain heightfield must provide a finite slope and surface normal.");

            const string spacesJson = "{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"chunk\":[0,0],\"revision\":1,\"spaces\":[{\"id\":\"house-1\",\"kind\":\"building\",\"frame\":0,\"origin\":{\"e\":10,\"n\":20,\"u\":0},\"bounds\":{\"min_e\":10,\"min_n\":20,\"min_u\":-4,\"max_e\":20,\"max_n\":30,\"max_u\":8},\"floors\":[{\"id\":\"basement\",\"kind\":\"basement\",\"elevation_m\":-3,\"walkable\":true},{\"id\":\"ground\",\"kind\":\"ground\",\"elevation_m\":0,\"walkable\":true},{\"id\":\"upper\",\"kind\":\"floor\",\"elevation_m\":3.2,\"walkable\":true}],\"surfaces\":[{\"id\":\"floor-1\",\"floor_id\":\"ground\",\"kind\":\"floor\",\"points\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":4,\"y\":0,\"z\":0},{\"x\":4,\"y\":0,\"z\":4}],\"collidable\":true,\"walkable\":true}],\"portals\":[{\"id\":\"stairs-door\",\"target_space_id\":\"house-1\",\"kind\":\"door\",\"e\":12,\"n\":20,\"u\":0,\"width_m\":1,\"height_m\":2,\"open\":true}],\"stairs\":[{\"id\":\"stair-1\",\"from_floor\":\"ground\",\"to_floor\":\"upper\",\"kind\":\"wooden\",\"width_m\":1,\"step_count\":12,\"points\":[{\"x\":2,\"y\":0,\"z\":1},{\"x\":2,\"y\":3.2,\"z\":3}]}]}]}";
            var spaces = Newtonsoft.Json.JsonConvert.DeserializeObject<Pogranichye.Client.Networking.Protocol.WorldSpaceChunkFrame>(spacesJson);
            if (!Pogranichye.Client.Networking.WorldSpaceDescriptorValidator.IsValid(spaces) ||
                spaces.Spaces.Count != 1 || spaces.Spaces[0].Floors.Count != 3 ||
                spaces.Spaces[0].Stairs[0].StepCount != 12)
                throw new FormatException("Multi-level world-space descriptor validation failed.");

            const string invalidSpacesJson = "{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"chunk\":[0,0],\"spaces\":[{\"id\":\"bad\",\"kind\":\"cave\",\"frame\":0,\"origin\":{\"e\":0,\"n\":0,\"u\":0},\"bounds\":{\"min_e\":0,\"min_n\":0,\"min_u\":0,\"max_e\":1,\"max_n\":1,\"max_u\":1},\"stairs\":[{\"id\":\"bad-stair\",\"width_m\":-1,\"points\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":1,\"y\":1,\"z\":1}]}]}]}";
            var invalidSpaces = Newtonsoft.Json.JsonConvert.DeserializeObject<Pogranichye.Client.Networking.Protocol.WorldSpaceChunkFrame>(invalidSpacesJson);
            if (Pogranichye.Client.Networking.WorldSpaceDescriptorValidator.IsValid(invalidSpaces))
                throw new FormatException("Malformed space descriptor with negative stair width must be rejected.");
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
            const string frameJson = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"room\":{\"id\":\"9001\",\"settlement\":\"7\",\"door_poi\":\"tavern\",\"rent_price\":6,\"paid_through_day\":40,\"grace_until_day\":41,\"lease_state\":\"active\",\"indoor\":true,\"chest_cap\":40,\"chest_load\":1,\"chest_items\":{\"torch\":1}},\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":1,\"n\":1,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[0,0],\"layer\":1}},\"place\":{\"anchor\":{\"frame\":0,\"e\":0,\"n\":0,\"u\":120},\"rooms\":{\"total\":2,\"vacant\":1,\"rent_price\":6,\"items\":[{\"id\":\"9001\",\"lease_state\":\"vacant\",\"rent_price\":6,\"available\":true},{\"id\":\"9002\",\"lease_state\":\"active\",\"rent_price\":6,\"available\":false}]},\"neighbors\":[{\"id\":\"43\",\"pos\":{\"x\":2,\"y\":3,\"facing\":90},\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":2,\"n\":3,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[0,0],\"layer\":1}}]}}";
            var frame = WorldFrameParser.Parse(frameJson);
            if (frame.Me.Room == null || frame.Me.Room.Id != "9001" ||
                frame.Me.Room.LeaseState != "active" || !frame.Me.Room.Indoor ||
                frame.Me.Room.ChestItems == null || frame.Me.Room.ChestItems["torch"] != 1)
                throw new FormatException("v1.20 me.room DTO deserialization failed.");
            if (frame.Place.Rooms == null || frame.Place.Rooms.Total != 2 ||
                frame.Place.Rooms.Vacant != 1 || frame.Place.Rooms.RentPrice != 6d ||
                frame.Place.Rooms.Items == null || frame.Place.Rooms.Items.Count != 2 ||
                frame.Place.Rooms.Items[0].Id != "9001" || frame.Place.Rooms.Items[0].LeaseState != "vacant" ||
                !frame.Place.Rooms.Items[0].Available || frame.Place.Rooms.Items[1].Available)
                throw new FormatException("v1.20 place.rooms DTO deserialization failed.");
            if (frame.Place.Neighbors == null || frame.Place.Neighbors.Count != 1 ||
                frame.Place.Neighbors[0].WorldPosition == null || !frame.Place.Neighbors[0].WorldPosition.IsValid)
                throw new FormatException("v1.20 neighbors[].wpos DTO deserialization failed.");

            var legacy = WorldFrameParser.Parse("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"},\"place\":{\"id\":\"7\"}}");
            if (legacy.Me.Room != null || legacy.Place.Rooms != null)
                throw new FormatException("v1.0-v1.19 frames without v1.20 room fields must remain compatible.");

            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"},\"place\":{\"anchor\":{\"frame\":0,\"e\":0,\"n\":0,\"u\":0},\"neighbors\":[{\"id\":\"43\",\"pos\":{\"x\":2,\"y\":3,\"facing\":90},\"wpos\":{\"scheme\":\"yof-tangent\",\"v\":1,\"frame\":0,\"e\":300,\"n\":3,\"u\":120,\"ground\":120,\"z\":0,\"chunk\":[1,0],\"layer\":1}}]}}", "neighbor wpos inconsistent with local pos");
        }

        private static void ValidateClaudeV131V133Dtos()
        {
            const string frameJson = "{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\",\"mode\":{\"selected\":\"work\",\"goal\":\"work\",\"act\":\"carpentry\",\"act_code\":\"carpentry\",\"deviates\":false,\"options\":[{\"id\":\"work\",\"name\":\"Трудиться\"}]},\"vigor\":{\"energy\":72,\"per_hour\":-2.2,\"stamina\":{\"now\":0.65,\"regen_per_s\":0.02,\"drain_per_s\":0.05}},\"horses\":[{\"id\":\"h1\",\"name\":\"Гнедой\",\"stam\":85,\"mounted\":false,\"active\":true,\"here\":true,\"bag\":{},\"bag_load\":0,\"bag_cap\":40,\"settlement_id\":\"7\",\"settlement_name\":\"Великоград\",\"realm_name\":\"Великоградское княжество\"}]},\"place\":{\"id\":\"7\",\"lair\":{\"since_day\":12,\"bandits\":3,\"names\":[\"Ратибор\",\"Мирон\"]}}}";
            var frame = WorldFrameParser.Parse(frameJson);
            if (frame.Me.Horses == null || frame.Me.Horses.Count != 1 ||
                frame.Me.Horses[0].SettlementName != "Великоград" ||
                frame.Me.Horses[0].RealmName != "Великоградское княжество" ||
                !frame.Me.Horses[0].Active || frame.Place.Lair == null ||
                frame.Place.Lair.Bandits != 3 || frame.Place.Lair.Names == null ||
                frame.Place.Lair.Names.Count != 2 ||
                frame.Me.Mode == null || frame.Me.Mode.Selected != "work" ||
                frame.Me.Mode.Options == null || frame.Me.Mode.Options.Count != 1 ||
                frame.Me.Vigor == null || frame.Me.Vigor.Stamina == null ||
                Math.Abs(frame.Me.Vigor.Stamina.DrainPerSecond - 0.05d) > 0.0001d)
                throw new FormatException("v1.29 life mode/vigor, v1.31 bandit lair and v1.32 horse herd DTO mapping failed.");

            const string layoutJson = "{\"version\":2,\"plan\":\"grid\",\"land\":{\"water\":\"well\",\"fields\":\"none\",\"forest\":\"none\"},\"buildings\":[{\"kind\":\"house\",\"x0\":-4,\"y0\":-4,\"x1\":4,\"y1\":4,\"storeys\":4,\"height_m\":12.8}],\"zones\":[]}";
            var layout = Newtonsoft.Json.JsonConvert.DeserializeObject<WorldLayoutFrame>(layoutJson);
            if (layout == null || layout.Plan != "grid" || layout.Land == null ||
                layout.Land.Water != "well" || layout.Land.Fields != "none" ||
                layout.Buildings == null || layout.Buildings.Count != 1 ||
                layout.Buildings[0].Storeys != 4 || Math.Abs(layout.Buildings[0].HeightMeters - 12.8d) > 0.001d)
                throw new FormatException("v1.33 grid plan, land profile and multi-storey building DTO mapping failed.");
            Pogranichye.Client.Networking.WorldLandFrameParser.Validate(layout.Land);
            var cityPoiIds = new HashSet<string>(StringComparer.Ordinal)
            { "tavern", "well", "hall", "bazaar", "weaver", "tannery", "bakery", "carpenter", "potter", "guard", "caravanserai", "inn" };
            if (!Pogranichye.Client.Networking.WorldLayoutCompatibilityPolicy.HasRequiredPois("grid", cityPoiIds))
                throw new FormatException("v1.33 grid city POI compatibility policy rejected the city layout.");

            const string worldJson = "{\"scheme\":\"yof-tangent\",\"v\":1,\"units\":\"m\",\"chunk_m\":256,\"layer_m\":64,\"settlements\":[{\"id\":\"7\",\"frame\":0,\"e\":0,\"n\":0,\"u\":0,\"bandit_lair\":true}]}";
            var world = Newtonsoft.Json.JsonConvert.DeserializeObject<WorldManifestFrame>(worldJson);
            if (world?.Settlements == null || world.Settlements.Count != 1 || !world.Settlements[0].BanditLair)
                throw new FormatException("v1.31 world.settlements[].bandit_lair DTO mapping failed.");

            var legacy = WorldFrameParser.Parse("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"42\"},\"place\":{\"id\":\"7\"}}");
            if (legacy.Me.Horses != null || legacy.Place.Lair != null)
                throw new FormatException("Legacy v1 frames without v1.31/v1.32 fields must remain compatible.");
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
