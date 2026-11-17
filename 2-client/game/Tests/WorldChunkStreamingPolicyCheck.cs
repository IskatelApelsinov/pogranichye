using System;
using System.Collections.Generic;
using Pogranichye.Client.World;

internal static class WorldChunkStreamingPolicyCheck
{
    private static int _checks;
    private static void True(bool value, string label)
    {
        _checks++;
        if (!value) throw new Exception(label);
    }
    private static void Equal<T>(T expected, T actual, string label)
    {
        _checks++;
        if (!Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", got " + actual);
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
        var center = new WorldChunkCoordinate(0, -4, 7);
        var plan = WorldChunkStreamingPolicy.BuildRequestPlan(center, 1);
        Equal(9, plan.Count, "3x3 request window");
        Equal(center, plan[0], "center requested first");
        var unique = new HashSet<WorldChunkCoordinate>(plan);
        Equal(plan.Count, unique.Count, "no duplicate requests");
        var repeated = WorldChunkStreamingPolicy.BuildRequestPlan(center, 1);
        for (var i = 0; i < plan.Count; i++)
            Equal(plan[i], repeated[i], "deterministic order " + i);

        True(WorldChunkStreamingPolicy.IsWithinRetention(center,
            new WorldChunkCoordinate(0, -6, 9), 2), "retention includes square halo corner");
        True(!WorldChunkStreamingPolicy.IsWithinRetention(center,
            new WorldChunkCoordinate(0, -7, 7), 2), "retention excludes outside X");
        True(!WorldChunkStreamingPolicy.IsWithinRetention(center,
            new WorldChunkCoordinate(1, -4, 7), 2), "retention rejects other frame");
        True(WorldChunkStreamingPolicy.IsWithinRequest(center,
            new WorldChunkCoordinate(0, -5, 8), 1), "request membership");
        Throws<ArgumentOutOfRangeException>(() => WorldChunkStreamingPolicy.BuildRequestPlan(center, -1),
            "negative request radius");
        Throws<ArgumentOutOfRangeException>(() => WorldChunkStreamingPolicy.IsWithinRetention(center, center, -1),
            "negative retention radius");

        Console.WriteLine("World chunk streaming policy checks passed: " + _checks);
        return 0;
    }
}