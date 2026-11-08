#if UNITY_EDITOR
using System;
using System.IO;
using Pogranichye.Client.Networking;
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

                ValidateNegativeCases();
                Debug.Log($"v1.8 fixture valid. Player ID: {frame.Me.Id}; seq: {frame.Me.Position.Sequence}; neighbors: {frame.Place.Neighbors?.Count ?? 0}. Strict negative cases passed.");
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
