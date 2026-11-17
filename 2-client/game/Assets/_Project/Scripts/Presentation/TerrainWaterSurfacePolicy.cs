namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Resolves water surface vertices from explicit server samples when present.
    /// Legacy water descriptors still use the documented visual lift over their estimated level.
    /// This is presentation only; it does not infer bed depth or grant swimming authority.
    /// </summary>
    public static class TerrainWaterSurfacePolicy
    {
        public static double ResolveLocalHeight(double? serverSurfaceMeters,
            double fallbackWaterLevelMeters, double anchorUpMeters, double legacyVisualLiftMeters)
        {
            if (serverSurfaceMeters.HasValue && IsFinite(serverSurfaceMeters.Value))
                return serverSurfaceMeters.Value - anchorUpMeters;

            if (!IsFinite(fallbackWaterLevelMeters) || !IsFinite(anchorUpMeters) ||
                !IsFinite(legacyVisualLiftMeters))
                return 0d;

            return fallbackWaterLevelMeters + legacyVisualLiftMeters - anchorUpMeters;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
