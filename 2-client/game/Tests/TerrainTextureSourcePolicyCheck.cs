using System;
using Pogranichye.Client.Presentation;

public static class TerrainTextureSourcePolicyCheck
{
    public static int Main()
    {
        Check(TerrainTextureSourcePolicy.Select(true, true) == TerrainTextureSourcePolicy.Source.Authored,
            "authored terrain maps must take priority over Grok");
        Check(TerrainTextureSourcePolicy.Select(true, false) == TerrainTextureSourcePolicy.Source.Authored,
            "authored terrain maps must be used when Grok is absent");
        Check(TerrainTextureSourcePolicy.Select(false, true) == TerrainTextureSourcePolicy.Source.Grok,
            "Grok maps must be used when authored terrain maps are absent");
        Check(TerrainTextureSourcePolicy.Select(false, false) == TerrainTextureSourcePolicy.Source.ProceduralFallback,
            "procedural terrain must remain the final fallback");
        Console.WriteLine("Terrain texture source policy checks passed.");
        return 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
