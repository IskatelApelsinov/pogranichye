using System;
using System.Collections.Generic;

namespace Pogranichye.Client.World
{
    public enum HexCellStreamingTier
    {
        Full = 0,
        Prefetch = 1
    }

    public struct HexCellStreamingEntry
    {
        public readonly HexCellCoordinate Cell;
        public readonly int Ring;
        public readonly HexCellStreamingTier Tier;

        public HexCellStreamingEntry(HexCellCoordinate cell, int ring, HexCellStreamingTier tier)
        {
            Cell = cell;
            Ring = ring;
            Tier = tier;
        }
    }

    /// <summary>
    /// Deterministic logical-cell streaming plan. Ring counts are topological, not metres.
    /// It does not fetch or unload terrain; existing square chunk streaming remains authoritative.
    /// </summary>
    public static class HexCellStreamingPolicy
    {
        public static List<HexCellCoordinate> GetRing(HexCellCoordinate center, int ring)
        {
            if (ring < 0) throw new ArgumentOutOfRangeException("ring");
            var result = new List<HexCellCoordinate>(ring == 0 ? 1 : checked(6 * ring));
            if (ring == 0)
            {
                result.Add(center);
                return result;
            }

            // Walk a fixed side order around the axial ring. Starting at direction 4
            // gives a stable first cell and each subsequent side is traversed once.
            var directions = new[]
            {
                new[] { 1L, 0L }, new[] { 1L, -1L }, new[] { 0L, -1L },
                new[] { -1L, 0L }, new[] { -1L, 1L }, new[] { 0L, 1L }
            };
            var q = checked(center.Q + directions[4][0] * ring);
            var r = checked(center.R + directions[4][1] * ring);
            for (var side = 0; side < 6; side++)
            {
                for (var step = 0; step < ring; step++)
                {
                    result.Add(new HexCellCoordinate(center.Frame, q, r));
                    q = checked(q + directions[side][0]);
                    r = checked(r + directions[side][1]);
                }
            }
            return result;
        }

        public static List<HexCellCoordinate> GetDisk(HexCellCoordinate center, int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException("radius");
            var count = checked(1 + 3 * radius * (radius + 1));
            var result = new List<HexCellCoordinate>(count);
            for (var ring = 0; ring <= radius; ring++)
                result.AddRange(GetRing(center, ring));
            return result;
        }

        public static List<HexCellStreamingEntry> BuildPlan(
            HexCellCoordinate center, int fullRadius, int prefetchRadius)
        {
            if (fullRadius < 0) throw new ArgumentOutOfRangeException("fullRadius");
            if (prefetchRadius < fullRadius)
                throw new ArgumentOutOfRangeException("prefetchRadius");

            var cells = GetDisk(center, prefetchRadius);
            var plan = new List<HexCellStreamingEntry>(cells.Count);
            for (var i = 0; i < cells.Count; i++)
            {
                var ring = checked((int)center.DistanceTo(cells[i]));
                var tier = ring <= fullRadius ? HexCellStreamingTier.Full : HexCellStreamingTier.Prefetch;
                plan.Add(new HexCellStreamingEntry(cells[i], ring, tier));
            }
            return plan;
        }
    }
}
