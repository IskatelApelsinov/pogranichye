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

                ValidateNegativeCases();
                Debug.Log($"v1 fixture valid. Player ID: {frame.Me.Id}; neighbors: {frame.Place.Neighbors?.Count ?? 0}. Strict negative cases passed.");
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
            ExpectRejected("{\"v\":1,\"alive\":true,\"me\":{\"id\":\"18446744073709551616\"}}", "overflowing me.id");
            ExpectRejected("{\"v\":1,\"me\":{\"id\":\"42\"}}", "missing alive");
            ExpectRejected("{\"v\":\"1\",\"alive\":false}", "string version");
            ExpectRejected("{\"v\":2,\"alive\":false}", "unsupported version");

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
