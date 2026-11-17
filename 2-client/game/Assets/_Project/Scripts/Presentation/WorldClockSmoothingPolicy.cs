namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Smooths presentation-only movement between discrete authoritative server-hour samples.
    /// It never advances or modifies the server clock.
    /// </summary>
    public static class WorldClockSmoothingPolicy
    {
        public static float AdvancePhase(float currentPhase, float targetPhase, float deltaTime, float degreesPerSecond)
        {
            if (float.IsNaN(currentPhase) || float.IsInfinity(currentPhase)) currentPhase = 0f;
            if (float.IsNaN(targetPhase) || float.IsInfinity(targetPhase)) targetPhase = currentPhase;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f)
                return Repeat01(currentPhase);
            if (float.IsNaN(degreesPerSecond) || float.IsInfinity(degreesPerSecond) || degreesPerSecond <= 0f)
                return Repeat01(currentPhase);

            var current = Repeat01(currentPhase);
            var target = Repeat01(targetPhase);
            var delta = target - current;
            if (delta > 0.5f) delta -= 1f;
            else if (delta < -0.5f) delta += 1f;

            var maxStep = degreesPerSecond * deltaTime / 360f;
            if (delta > maxStep) delta = maxStep;
            else if (delta < -maxStep) delta = -maxStep;

            return Repeat01(current + delta);
        }

        private static float Repeat01(float value)
        {
            value -= (float)System.Math.Floor(value);
            return value < 0f ? value + 1f : value;
        }
    }
}
