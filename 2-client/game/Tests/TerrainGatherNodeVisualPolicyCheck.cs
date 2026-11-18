using System;
using Pogranichye.Client.Presentation;

internal static class TerrainGatherNodeVisualPolicyCheck
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static int Main()
    {
        foreach (var kind in new[] { "herb", "mushroom", "berries", "hive", "deadwood", "nest", "flax" })
            Require(TerrainGatherNodeVisualPolicy.IsKnownKind(kind), "known gather kind: " + kind);

        Require(!TerrainGatherNodeVisualPolicy.IsKnownKind("future_kind"), "unknown kinds do not get invented models");
        Require(TerrainGatherNodeVisualPolicy.IsRenderable("g:-2:4", "herb", 0, 0, 0, 256),
            "depleted but valid node keeps a refreshable visual");
        Require(!TerrainGatherNodeVisualPolicy.IsRenderable("site:1", "herb", 1, 1, 1, 256),
            "stable gather ID required");
        Require(!TerrainGatherNodeVisualPolicy.IsRenderable("g:1:2", "future_kind", 1, 1, 1, 256),
            "unknown kind is not rendered");
        Require(!TerrainGatherNodeVisualPolicy.IsRenderable("g:1:2", "herb", 256, 1, 1, 256),
            "east edge is exclusive");
        Require(!TerrainGatherNodeVisualPolicy.IsRenderable("g:1:2", "herb", 1, double.NaN, 1, 256),
            "finite chunk coordinates required");
        Require(!TerrainGatherNodeVisualPolicy.IsRenderable("g:1:2", "herb", 1, 1, -1, 256),
            "nonnegative server stock required");
        Require(TerrainGatherNodeVisualPolicy.ShouldBeVisible(1), "available node is visible");
        Require(!TerrainGatherNodeVisualPolicy.ShouldBeVisible(0), "depleted node is hidden");
        Require(!TerrainGatherNodeVisualPolicy.ShouldBeVisible(double.NaN), "non-finite stock is hidden");

        Console.WriteLine("PASS: terrain gather-node visual policy");
        return 0;
    }
}
