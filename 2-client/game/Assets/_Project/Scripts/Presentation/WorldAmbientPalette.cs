namespace Pogranichye.Client.Presentation
{
    public struct AmbientRgb
    {
        public readonly float R, G, B;
        public AmbientRgb(float r, float g, float b) { R = r; G = g; B = b; }
        public static AmbientRgb Lerp(AmbientRgb from, AmbientRgb to, float amount)
        {
            var t = amount < 0f ? 0f : amount > 1f ? 1f : amount;
            return new AmbientRgb(from.R + (to.R - from.R) * t,
                from.G + (to.G - from.G) * t, from.B + (to.B - from.B) * t);
        }
    }
    public struct WorldAmbientSample
    {
        public readonly AmbientRgb Sky, Horizon, Ground;
        public WorldAmbientSample(AmbientRgb sky, AmbientRgb horizon, AmbientRgb ground)
        { Sky = sky; Horizon = horizon; Ground = ground; }
    }
    /// <summary>Cheap directional ambient approximation for runtime-generated settlements; not GI.</summary>
    public static class WorldAmbientPalette
    {
        private static readonly AmbientRgb NightSky = new AmbientRgb(0.035f, 0.050f, 0.085f);
        private static readonly AmbientRgb NightHorizon = new AmbientRgb(0.026f, 0.032f, 0.050f);
        private static readonly AmbientRgb NightGround = new AmbientRgb(0.012f, 0.016f, 0.025f);
        private static readonly AmbientRgb DaySky = new AmbientRgb(0.36f, 0.44f, 0.54f);
        private static readonly AmbientRgb DayHorizon = new AmbientRgb(0.27f, 0.30f, 0.29f);
        private static readonly AmbientRgb DayGround = new AmbientRgb(0.105f, 0.095f, 0.075f);
        private static readonly AmbientRgb DuskSky = new AmbientRgb(0.38f, 0.22f, 0.19f);
        private static readonly AmbientRgb DuskHorizon = new AmbientRgb(0.34f, 0.19f, 0.15f);
        private static readonly AmbientRgb DuskGround = new AmbientRgb(0.105f, 0.070f, 0.060f);
        public static WorldAmbientSample Evaluate(float daylight, float dusk)
        {
            var day = Clamp01(daylight);
            var sunset = Clamp01(dusk) * 0.72f;
            return new WorldAmbientSample(
                AmbientRgb.Lerp(AmbientRgb.Lerp(NightSky, DaySky, day), DuskSky, sunset),
                AmbientRgb.Lerp(AmbientRgb.Lerp(NightHorizon, DayHorizon, day), DuskHorizon, sunset),
                AmbientRgb.Lerp(AmbientRgb.Lerp(NightGround, DayGround, day), DuskGround, sunset));
        }
        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
