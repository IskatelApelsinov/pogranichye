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
                if (frame.Me.Inventory == null || frame.Me.Inventory.Items == null || frame.Me.World == null)
                    throw new FormatException("The shared v1.7 fixture must include inventory and world data.");
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
                if (frame.Place.Life == null || frame.Place.Life.Population < 0 || frame.Place.Life.Working < 0 ||
                    frame.Place.Life.Activities == null || frame.Place.Life.Stock == null || frame.Place.Life.Activities.Maintenance < 0 ||
                    frame.Place.Life.Stock.Wood < 0 || frame.Place.Life.Stock.Metal < 0 || frame.Place.Beer < 0)
                    throw new FormatException("The shared v1.18 fixture must include a valid settlement life summary.");

                ValidateNegativeCases();
                ValidateJournalCases();
                ValidateMapProjection();
                ValidateWorldV119Dtos();
                Debug.Log($"v1.19 fixture valid. Player ID: {frame.Me.Id}; seq: {frame.Me.Position.Sequence}; neighbors: {frame.Place.Neighbors?.Count ?? 0}; settlement population: {frame.Place.Life.Population}. Strict parser and map projection checks passed.");
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

        private static void ValidateMapProjection()
        {
            var map = new Rect(10f, 20f, 300f, 300f);
            AssertMapPoint(WorldMapProjection.ToMapPoint(-150d, -150d, 150d, map),
                new Vector2(10f, 320f), "south-west map corner");
            AssertMapPoint(WorldMapProjection.ToMapPoint(150d, 150d, 150d, map),
                new Vector2(310f, 20f), "north-east map corner");
            AssertMapPoint(WorldMapProjection.ToMapPoint(0d, 0d, 150d, map),
                new Vector2(160f, 170f), "map centre");

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
