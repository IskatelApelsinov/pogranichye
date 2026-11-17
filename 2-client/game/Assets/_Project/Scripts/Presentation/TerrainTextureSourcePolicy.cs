namespace Pogranichye.Client.Presentation
{
    /// <summary>Defines the deterministic fallback order for streamed-world terrain textures.</summary>
    public static class TerrainTextureSourcePolicy
    {
        public enum Source
        {
            ProceduralFallback = 0,
            Grok = 1,
            Authored = 2
        }

        public static Source Select(bool authoredAvailable, bool grokAvailable)
        {
            if (authoredAvailable) return Source.Authored;
            if (grokAvailable) return Source.Grok;
            return Source.ProceduralFallback;
        }
    }
}
