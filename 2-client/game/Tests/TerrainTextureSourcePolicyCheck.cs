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
        Check(TerrainTextureSourcePolicy.HasMatchingNormal(TerrainTextureSourcePolicy.Source.Authored, true, true),
            "authored albedo may use its authored normal");
        Check(!TerrainTextureSourcePolicy.HasMatchingNormal(TerrainTextureSourcePolicy.Source.Authored, false, true),
            "authored albedo must not borrow an unrelated Grok normal");
        Check(TerrainTextureSourcePolicy.HasMatchingNormal(TerrainTextureSourcePolicy.Source.Grok, true, true),
            "Grok albedo may use its paired Grok normal");
        Check(!TerrainTextureSourcePolicy.HasMatchingNormal(TerrainTextureSourcePolicy.Source.Grok, true, false),
            "Grok albedo must not borrow an unrelated authored normal");
        Check(!TerrainTextureSourcePolicy.HasMatchingNormal(TerrainTextureSourcePolicy.Source.ProceduralFallback, true, true),
            "procedural albedo must not use an unrelated normal map");
        Console.WriteLine("Terrain texture source policy checks passed.");
        return 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
