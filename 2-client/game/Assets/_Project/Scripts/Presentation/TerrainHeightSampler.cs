using System;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Read-only sampling of server heightfields for local presentation (foot placement,
    /// slope shading, decals and debugging). Never grants or denies authoritative movement.
    /// </summary>
    public static class TerrainHeightSampler
    {
        public static bool TrySample(TerrainChunkFrame chunk, double east, double north,
            out double heightMeters, out float slopeDegrees, out Vector3 normal)
        {
            heightMeters = 0d;
            slopeDegrees = 0f;
            normal = Vector3.up;
            if (chunk == null || !chunk.IsValid || chunk.Origin == null) return false;

            var gx = (east - chunk.Origin.East) / chunk.StepMeters;
            var gz = (north - chunk.Origin.North) / chunk.StepMeters;
            if (gx < 0d || gz < 0d || gx > chunk.Side - 1 || gz > chunk.Side - 1) return false;

            if (!TryHeight(chunk, gx, gz, out heightMeters)) return false;
            var left = Math.Max(0d, gx - 1d);
            var right = Math.Min(chunk.Side - 1d, gx + 1d);
            var south = Math.Max(0d, gz - 1d);
            var northSample = Math.Min(chunk.Side - 1d, gz + 1d);
            if (!TryHeight(chunk, right, gz, out var hRight) ||
                !TryHeight(chunk, left, gz, out var hLeft) ||
                !TryHeight(chunk, gx, northSample, out var hNorth) ||
                !TryHeight(chunk, gx, south, out var hSouth))
                return false;

            var dx = (hRight - hLeft) / Math.Max(0.001d, (right - left) * chunk.StepMeters);
            var dz = (hNorth - hSouth) / Math.Max(0.001d, (northSample - south) * chunk.StepMeters);
            var gradient = Math.Sqrt(dx * dx + dz * dz);
            slopeDegrees = (float)(Math.Atan(gradient) * Mathf.Rad2Deg);
            normal = new Vector3((float)-dx, 1f, (float)-dz).normalized;
            return true;
        }

        private static bool TryHeight(TerrainChunkFrame chunk, double gx, double gz, out double height)
        {
            height = 0d;
            var x0 = (int)Math.Floor(gx);
            var z0 = (int)Math.Floor(gz);
            var x1 = Math.Min(chunk.Side - 1, x0 + 1);
            var z1 = Math.Min(chunk.Side - 1, z0 + 1);
            var tx = gx - x0;
            var tz = gz - z0;
            var h00 = chunk.Heights[z0 * chunk.Side + x0];
            var h10 = chunk.Heights[z0 * chunk.Side + x1];
            var h01 = chunk.Heights[z1 * chunk.Side + x0];
            var h11 = chunk.Heights[z1 * chunk.Side + x1];
            height = (h00 * (1d - tx) + h10 * tx) * (1d - tz) +
                     (h01 * (1d - tx) + h11 * tx) * tz;
            return !double.IsNaN(height) && !double.IsInfinity(height);
        }
    }
}
