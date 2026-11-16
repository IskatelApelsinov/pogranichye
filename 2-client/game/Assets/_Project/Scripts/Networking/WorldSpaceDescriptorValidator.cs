using System;
using System.Collections.Generic;
using Pogranichye.Client.Networking.Protocol;

namespace Pogranichye.Client.Networking
{
    /// <summary>
    /// Rejects malformed server-authored multi-level geometry before presentation builders consume it.
    /// This validates presentation descriptors only; it never grants movement authority.
    /// </summary>
    public static class WorldSpaceDescriptorValidator
    {
        private const double BoundsEpsilon = 0.002d;
        private const double StairElevationEpsilon = 0.01d;

        public static bool IsValid(WorldSpaceChunkFrame chunk)
        {
            if (chunk == null || !chunk.IsValid) return false;

            var spaceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var space in chunk.Spaces)
            {
                if (space == null || string.IsNullOrWhiteSpace(space.Id) ||
                    !spaceIds.Add(space.Id) || string.IsNullOrWhiteSpace(space.Kind) ||
                    space.Frame != chunk.Frame || !Valid(space.Origin) || !ValidVolume(space.Bounds))
                    return false;

                var floors = new Dictionary<string, double>(StringComparer.Ordinal);
                if (space.Floors != null)
                {
                    foreach (var floor in space.Floors)
                    {
                        if (floor == null || string.IsNullOrWhiteSpace(floor.Id) ||
                            floors.ContainsKey(floor.Id) || !Finite(floor.ElevationMeters) ||
                            !Between(floor.ElevationMeters, space.Bounds.MinUp, space.Bounds.MaxUp))
                            return false;
                        floors.Add(floor.Id, floor.ElevationMeters);
                    }
                }

                var surfaceIds = new HashSet<string>(StringComparer.Ordinal);
                if (space.Surfaces != null)
                {
                    foreach (var surface in space.Surfaces)
                    {
                        if (surface == null || string.IsNullOrWhiteSpace(surface.Id) ||
                            !surfaceIds.Add(surface.Id) || string.IsNullOrWhiteSpace(surface.Kind) ||
                            !HasFloor(floors, surface.FloorId) ||
                            !ValidPolygon(surface.Points) ||
                            !PointsInside(space, surface.Points))
                            return false;
                    }
                }

                var roomIds = new HashSet<string>(StringComparer.Ordinal);
                if (space.Rooms != null)
                {
                    foreach (var room in space.Rooms)
                    {
                        if (room == null || string.IsNullOrWhiteSpace(room.Id) ||
                            !roomIds.Add(room.Id) || string.IsNullOrWhiteSpace(room.Kind) ||
                            !HasFloor(floors, room.FloorId) || !ValidVolume(room.Bounds) ||
                            !BoundsInside(space.Bounds, room.Bounds))
                            return false;
                    }
                }

                var portalIds = new HashSet<string>(StringComparer.Ordinal);
                if (space.Portals != null)
                {
                    foreach (var portal in space.Portals)
                    {
                        if (portal == null || string.IsNullOrWhiteSpace(portal.Id) ||
                            !portalIds.Add(portal.Id) ||
                            !Finite(portal.East) || !Finite(portal.North) || !Finite(portal.Up) ||
                            !Positive(portal.WidthMeters) || !Positive(portal.HeightMeters) ||
                            !PointInside(space.Bounds, portal.East, portal.North, portal.Up))
                            return false;
                    }
                }

                var stairIds = new HashSet<string>(StringComparer.Ordinal);
                if (space.Stairs != null)
                {
                    foreach (var stair in space.Stairs)
                    {
                        if (stair == null || string.IsNullOrWhiteSpace(stair.Id) ||
                            !stairIds.Add(stair.Id) || !Positive(stair.WidthMeters) ||
                            stair.StepCount < 0 ||
                            (!string.Equals(stair.Kind, "ramp", StringComparison.OrdinalIgnoreCase) &&
                             stair.StepCount == 0) ||
                            !HasFloor(floors, stair.FromFloor) || !HasFloor(floors, stair.ToFloor) ||
                            string.Equals(stair.FromFloor, stair.ToFloor, StringComparison.Ordinal) ||
                            !ValidPolyline(stair.Points) ||
                            !PointsInside(space, stair.Points) ||
                            !StairConnectsFloors(space, stair, floors))
                            return false;
                    }
                }
            }
            return true;
        }

        private static bool HasFloor(Dictionary<string, double> floors, string id)
        {
            return !string.IsNullOrWhiteSpace(id) && floors.ContainsKey(id);
        }

        private static bool Valid(WorldSpaceOriginFrame value)
        {
            return value != null && Finite(value.East) && Finite(value.North) && Finite(value.Up);
        }

        private static bool ValidVolume(WorldSpaceBoundsFrame value)
        {
            return value != null &&
                Finite(value.MinEast) && Finite(value.MinNorth) && Finite(value.MinUp) &&
                Finite(value.MaxEast) && Finite(value.MaxNorth) && Finite(value.MaxUp) &&
                value.MinEast < value.MaxEast &&
                value.MinNorth < value.MaxNorth &&
                value.MinUp < value.MaxUp;
        }

        private static bool ValidPoints(List<WorldSpacePointFrame> points, int minimumCount)
        {
            if (points == null || points.Count < minimumCount) return false;
            foreach (var point in points)
                if (point == null || !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z))
                    return false;
            return true;
        }

        // MeshCollider cannot represent a zero-area polygon. Check every edge as well as
        // Newell's area vector so duplicate/collinear server vertices are rejected early.
        private static bool ValidPolygon(List<WorldSpacePointFrame> points)
        {
            const double minimumSegmentLengthSquared = 1e-8d;
            const double minimumArea = 1e-6d;
            if (!ValidPoints(points, 3)) return false;

            double nx = 0d, ny = 0d, nz = 0d;
            for (var i = 0; i < points.Count; i++)
            {
                var current = points[i];
                var next = points[(i + 1) % points.Count];
                var dx = next.X - current.X;
                var dy = next.Y - current.Y;
                var dz = next.Z - current.Z;
                if (dx * dx + dy * dy + dz * dz <= minimumSegmentLengthSquared)
                    return false;

                nx += (current.Y - next.Y) * (current.Z + next.Z);
                ny += (current.Z - next.Z) * (current.X + next.X);
                nz += (current.X - next.X) * (current.Y + next.Y);
            }

            var areaVectorSquared = nx * nx + ny * ny + nz * nz;
            return areaVectorSquared > 4d * minimumArea * minimumArea;
        }

        private static bool ValidPolyline(List<WorldSpacePointFrame> points)
        {
            const double minimumSegmentLengthSquared = 1e-8d;
            if (!ValidPoints(points, 2)) return false;
            for (var i = 1; i < points.Count; i++)
            {
                var previous = points[i - 1];
                var current = points[i];
                var dx = current.X - previous.X;
                var dy = current.Y - previous.Y;
                var dz = current.Z - previous.Z;
                if (dx * dx + dy * dy + dz * dz <= minimumSegmentLengthSquared)
                    return false;
            }
            return true;
        }

        private static bool PointsInside(WorldSpaceFrame space, List<WorldSpacePointFrame> points)
        {
            foreach (var point in points)
            {
                if (!PointInside(space.Bounds,
                    space.Origin.East + point.X,
                    space.Origin.North + point.Z,
                    space.Origin.Up + point.Y))
                    return false;
            }
            return true;
        }

        private static bool StairConnectsFloors(WorldSpaceFrame space, WorldSpaceStairFrame stair,
            Dictionary<string, double> floors)
        {
            var first = stair.Points[0];
            var last = stair.Points[stair.Points.Count - 1];
            var startUp = space.Origin.Up + first.Y;
            var endUp = space.Origin.Up + last.Y;
            return Math.Abs(startUp - floors[stair.FromFloor]) <= StairElevationEpsilon &&
                   Math.Abs(endUp - floors[stair.ToFloor]) <= StairElevationEpsilon;
        }

        private static bool BoundsInside(WorldSpaceBoundsFrame outer, WorldSpaceBoundsFrame inner)
        {
            return inner.MinEast >= outer.MinEast - BoundsEpsilon &&
                   inner.MinNorth >= outer.MinNorth - BoundsEpsilon &&
                   inner.MinUp >= outer.MinUp - BoundsEpsilon &&
                   inner.MaxEast <= outer.MaxEast + BoundsEpsilon &&
                   inner.MaxNorth <= outer.MaxNorth + BoundsEpsilon &&
                   inner.MaxUp <= outer.MaxUp + BoundsEpsilon;
        }

        private static bool PointInside(WorldSpaceBoundsFrame bounds, double east, double north, double up)
        {
            return east >= bounds.MinEast - BoundsEpsilon && east <= bounds.MaxEast + BoundsEpsilon &&
                   north >= bounds.MinNorth - BoundsEpsilon && north <= bounds.MaxNorth + BoundsEpsilon &&
                   up >= bounds.MinUp - BoundsEpsilon && up <= bounds.MaxUp + BoundsEpsilon;
        }

        private static bool Between(double value, double minimum, double maximum)
        {
            return value >= minimum - BoundsEpsilon && value <= maximum + BoundsEpsilon;
        }

        private static bool Positive(double value) => Finite(value) && value > 0d;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
