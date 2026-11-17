using System;
using Pogranichye.Client.Presentation;

internal static class TerrainResourceVisualPolicyCheck
{
    private static int _assertions;

    private static void Equal(TerrainResourceVisualKind expected, TerrainResourceVisualKind actual, string message)
    {
        _assertions++;
        if (expected != actual)
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
    }

    public static void Main()
    {
        Equal(TerrainResourceVisualKind.LogPile, TerrainResourceVisualPolicy.Resolve("wood"), "wood uses a log pile");
        Equal(TerrainResourceVisualKind.BerryBush, TerrainResourceVisualPolicy.Resolve("berries"), "berries use a bush");
        Equal(TerrainResourceVisualKind.BerryBush, TerrainResourceVisualPolicy.Resolve(" BERRIES "), "kind matching is normalized");
        Equal(TerrainResourceVisualKind.StoneCluster, TerrainResourceVisualPolicy.Resolve("stone"), "stone uses a rock cluster");
        Equal(TerrainResourceVisualKind.StoneCluster, TerrainResourceVisualPolicy.Resolve("ore"), "legacy ore hints remain visible");
        Equal(TerrainResourceVisualKind.Generic, TerrainResourceVisualPolicy.Resolve("veg"), "legacy vegetable hints keep the generic marker");
        Equal(TerrainResourceVisualKind.Generic, TerrainResourceVisualPolicy.Resolve("mushroom"), "unknown kinds do not invent a server flora contract");
        Equal(TerrainResourceVisualKind.Generic, TerrainResourceVisualPolicy.Resolve(null), "missing kinds use a safe generic marker");
        Console.WriteLine("TerrainResourceVisualPolicy: " + _assertions + " assertions passed");
    }
}
