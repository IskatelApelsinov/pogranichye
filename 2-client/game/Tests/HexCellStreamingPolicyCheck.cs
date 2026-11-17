using System;
using System.Collections.Generic;
using Pogranichye.Client.World;

internal static class HexCellStreamingPolicyCheck
{
    private static int _checks;
    private static void Equal<T>(T expected, T actual, string label)
    {
        _checks++;
        if (!Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", got " + actual);
    }
    private static void True(bool value, string label)
    {
        _checks++;
        if (!value) throw new Exception(label);
    }
    private static void Throws<T>(Action action, string label) where T : Exception
    {
        _checks++;
        try { action(); }
        catch (T) { return; }
        throw new Exception(label + ": expected " + typeof(T).Name);
    }

    public static int Main()
    {
        var center = new HexCellCoordinate(2, -4, 9);
        Equal(1, HexCellStreamingPolicy.GetDisk(center, 0).Count, "disk radius 0");
        Equal(7, HexCellStreamingPolicy.GetDisk(center, 1).Count, "disk radius 1");
        Equal(19, HexCellStreamingPolicy.GetDisk(center, 2).Count, "disk radius 2");
        Equal(37, HexCellStreamingPolicy.GetDisk(center, 3).Count, "disk radius 3");

        for (var ring = 0; ring <= 5; ring++)
        {
            var cells = HexCellStreamingPolicy.GetRing(center, ring);
            Equal(ring == 0 ? 1 : 6 * ring, cells.Count, "ring count " + ring);
            var unique = new HashSet<HexCellCoordinate>(cells);
            Equal(cells.Count, unique.Count, "ring has no duplicates " + ring);
            for (var i = 0; i < cells.Count; i++)
                Equal((long)ring, center.DistanceTo(cells[i]), "ring distance " + ring + "/" + i);
            var second = HexCellStreamingPolicy.GetRing(center, ring);
            for (var i = 0; i < cells.Count; i++)
                Equal(cells[i], second[i], "ring order deterministic " + ring + "/" + i);
        }

        var plan = HexCellStreamingPolicy.BuildPlan(center, 1, 2);
        Equal(19, plan.Count, "plan covers center plus two rings");
        var full = 0;
        var prefetch = 0;
        for (var i = 0; i < plan.Count; i++)
        {
            if (plan[i].Tier == HexCellStreamingTier.Full) full++;
            else prefetch++;
            Equal(plan[i].Ring <= 1 ? HexCellStreamingTier.Full : HexCellStreamingTier.Prefetch,
                plan[i].Tier, "tier matches ring");
        }
        Equal(7, full, "center and first ring are full");
        Equal(12, prefetch, "second ring is prefetch only");

        Throws<ArgumentOutOfRangeException>(() => HexCellStreamingPolicy.GetRing(center, -1), "negative ring");
        Throws<ArgumentOutOfRangeException>(() => HexCellStreamingPolicy.BuildPlan(center, 2, 1), "prefetch smaller than full");
        Console.WriteLine("Hex cell streaming policy checks passed: " + _checks);
        return 0;
    }
}
