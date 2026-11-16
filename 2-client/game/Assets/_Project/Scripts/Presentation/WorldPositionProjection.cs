using System;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Projects canonical global positions into the current player's settlement-local Unity frame.
    /// Global doubles are differenced before conversion to floats, avoiding large-world precision loss.
    /// </summary>
    public static class WorldPositionProjection
    {
        public static bool TryToUnityPosition(WorldPositionFrame worldPosition,
            WorldPositionFrame originWorldPosition, ScenePositionFrame originScenePosition,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (worldPosition == null || originWorldPosition == null || originScenePosition == null ||
                !worldPosition.IsValid || !originWorldPosition.IsValid ||
                worldPosition.Frame != originWorldPosition.Frame)
                return false;

            var east = originScenePosition.X + (worldPosition.East - originWorldPosition.East);
            var north = originScenePosition.Y + (worldPosition.North - originWorldPosition.North);
            var up = worldPosition.Up - originWorldPosition.Up;
            if (!IsFinite(east) || !IsFinite(north) || !IsFinite(up) ||
                Math.Abs(east) > 1000000d || Math.Abs(north) > 1000000d || Math.Abs(up) > 1000000d)
                return false;

            position = new Vector3((float)east, (float)up, (float)north);
            return true;
        }

        /// <summary>
        /// Uses the current settlement's geographic centre when available. Outside a settlement,
        /// the player's own global position becomes the floating origin; never substitute local pos.
        /// </summary>
        public static WorldAnchorFrame ResolveAnchor(WorldFrame frame)
        {
            if (frame?.Place?.Anchor != null) return frame.Place.Anchor;
            var worldPosition = frame?.Me?.WorldPosition;
            if (worldPosition == null || !worldPosition.IsValid) return null;
            return new WorldAnchorFrame
            {
                Frame = worldPosition.Frame,
                East = worldPosition.East,
                North = worldPosition.North,
                Up = worldPosition.Ground
            };
        }

        /// <summary>Projects canonical global E/N/U against the current server settlement anchor.</summary>
        public static bool TryToUnityPosition(WorldPositionFrame worldPosition,
            WorldAnchorFrame originAnchor, out Vector3 position)
        {
            position = Vector3.zero;
            if (worldPosition == null || originAnchor == null || !worldPosition.IsValid)
                return false;

            if (!WorldPositionProjectionPolicy.TryProject(
                worldPosition.Frame, worldPosition.East, worldPosition.North, worldPosition.Up,
                originAnchor.Frame, originAnchor.East, originAnchor.North, originAnchor.Up,
                out var east, out var up, out var north))
                return false;

            position = new Vector3((float)east, (float)up, (float)north);
            return true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
