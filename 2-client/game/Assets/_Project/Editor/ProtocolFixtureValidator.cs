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

                Debug.Log($"v1 fixture valid. Player ID: {frame.Me.Id}; neighbors: {frame.Place.Neighbors?.Count ?? 0}.");
            }
            catch (Exception ex)
            {
                Debug.LogError("v1 fixture validation failed: " + ex);
            }
        }
    }
}
#endif
