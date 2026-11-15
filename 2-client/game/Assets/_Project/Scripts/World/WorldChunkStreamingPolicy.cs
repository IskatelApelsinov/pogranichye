using System;
using System.Collections.Generic;

namespace Pogranichye.Client.World
{
    public struct WorldChunkCoordinate : IEquatable<WorldChunkCoordinate>
    {
        public readonly int Frame;
        public readonly long X;
        public readonly long Z;

        public WorldChunkCoordinate(int frame, long x, long z)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException("frame");
            Frame = frame;
            X = x;
            Z = z;
        }

        public bool Equals(WorldChunkCoordinate other)
        {
            return Frame == other.Frame && X == other.X && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is WorldChunkCoordinate && Equals((WorldChunkCoordinate)obj);
        }

        public override int GetHashCode()
        {
            unchecked { return ((Frame * 397) ^ X.GetHashCode()) * 397 ^ Z.GetHashCode(); }
        }

        public override string ToString() { return Frame + ":" + X + ":" + Z; }
    }

    /// <summary>
    /// Deterministic square-chunk request and retention policy. It only plans data work;
    /// it never creates Unity objects or changes authoritative world state.
    /// </summary>
    public static class WorldChunkStreamingPolicy
    {
        public static List<WorldChunkCoordinate> BuildRequestPlan(
            WorldChunkCoordinate center, int requestRadius)
        {
            if (requestRadius < 0) throw new ArgumentOutOfRangeException("requestRadius");
            var side = checked(requestRadius * 2 + 1);
            var result = new List<WorldChunkCoordinate>(checked(side * side));
            for (var dz = -requestRadius; dz <= requestRadius; dz++)
            for (var dx = -requestRadius; dx <= requestRadius; dx++)
                result.Add(new WorldChunkCoordinate(center.Frame,
                    checked(center.X + dx), checked(center.Z + dz)));

            result.Sort((a, b) =>
            {
                var adx = (decimal)a.X - center.X;
                var adz = (decimal)a.Z - center.Z;
                var bdx = (decimal)b.X - center.X;
                var bdz = (decimal)b.Z - center.Z;
                var distance = (adx * adx + adz * adz).CompareTo(bdx * bdx + bdz * bdz);
                if (distance != 0) return distance;
                var x = a.X.CompareTo(b.X);
                return x != 0 ? x : a.Z.CompareTo(b.Z);
            });
            return result;
        }

        public static bool IsWithinRetention(
            WorldChunkCoordinate center, WorldChunkCoordinate candidate, int retentionRadius)
        {
            if (retentionRadius < 0) throw new ArgumentOutOfRangeException("retentionRadius");
            if (center.Frame != candidate.Frame) return false;
            var dx = Math.Abs((decimal)candidate.X - center.X);
            var dz = Math.Abs((decimal)candidate.Z - center.Z);
            return dx <= retentionRadius && dz <= retentionRadius;
        }

        public static bool IsWithinRequest(
            WorldChunkCoordinate center, WorldChunkCoordinate candidate, int requestRadius)
        {
            return IsWithinRetention(center, candidate, requestRadius);
        }
    }
}