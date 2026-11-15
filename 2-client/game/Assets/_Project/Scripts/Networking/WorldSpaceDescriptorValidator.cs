using System;
using Pogranichye.Client.Networking.Protocol;

namespace Pogranichye.Client.Networking
{
    /// <summary>Rejects malformed server-authored multi-level geometry before presentation builders consume it.</summary>
    public static class WorldSpaceDescriptorValidator
    {
        public static bool IsValid(WorldSpaceChunkFrame chunk)
        {
            if (chunk == null || !chunk.IsValid) return false;
            foreach (var space in chunk.Spaces)
            {
                if (space == null || string.IsNullOrWhiteSpace(space.Id) || space.Frame != chunk.Frame ||
                    !Valid(space.Origin) || !Valid(space.Bounds))
                    return false;
                if (space.Floors != null)
                    foreach (var floor in space.Floors)
                        if (floor == null || string.IsNullOrWhiteSpace(floor.Id) || !Finite(floor.ElevationMeters))
                            return false;
                if (space.Surfaces != null)
                    foreach (var surface in space.Surfaces)
                        if (surface == null || string.IsNullOrWhiteSpace(surface.Id) || !ValidPoints(surface.Points))
                            return false;
                if (space.Portals != null)
                    foreach (var portal in space.Portals)
                        if (portal == null || string.IsNullOrWhiteSpace(portal.Id) ||
                            !Finite(portal.East) || !Finite(portal.North) || !Finite(portal.Up) ||
                            !PositiveOrZero(portal.WidthMeters) || !PositiveOrZero(portal.HeightMeters))
                            return false;
                if (space.Stairs != null)
                    foreach (var stair in space.Stairs)
                        if (stair == null || string.IsNullOrWhiteSpace(stair.Id) ||
                            !PositiveOrZero(stair.WidthMeters) || !ValidPoints(stair.Points))
                            return false;
            }
            return true;
        }

        private static bool Valid(WorldSpaceOriginFrame value) => value != null &&
            Finite(value.East) && Finite(value.North) && Finite(value.Up);

        private static bool Valid(WorldSpaceBoundsFrame value) => value != null &&
            Finite(value.MinEast) && Finite(value.MinNorth) && Finite(value.MinUp) &&
            Finite(value.MaxEast) && Finite(value.MaxNorth) && Finite(value.MaxUp) &&
            value.MinEast <= value.MaxEast && value.MinNorth <= value.MaxNorth && value.MinUp <= value.MaxUp;

        private static bool ValidPoints(System.Collections.Generic.List<WorldSpacePointFrame> points)
        {
            if (points == null || points.Count < 2) return false;
            foreach (var point in points)
                if (point == null || !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z)) return false;
            return true;
        }

        private static bool PositiveOrZero(double value) => Finite(value) && value >= 0d;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
