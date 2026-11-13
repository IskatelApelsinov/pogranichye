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

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
