using System;
namespace Pogranichye.Client.Presentation.Tests
{
    internal static class WorldClockSmoothingPolicyCheck
    {
        private static int _checks;
        private static void Assert(bool condition, string message)
        {
            _checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
        private static float Distance(float a, float b)
        {
            var d = Math.Abs(a - b);
            return Math.Min(d, 1f - d);
        }
        public static int Main()
        {
            // 23:00 -> 00:00 must move forward through midnight, not backwards through noon.
            var midnight = WorldClockSmoothingPolicy.AdvancePhase(23f / 24f, 0f, 0.25f, 12f);
            Assert(midnight > 23f / 24f || midnight < 0.01f, "23:00 should advance through midnight");
            Assert(Distance(midnight, 23f / 24f) <= (12f * 0.25f / 360f) + 0.00001f, "forward step is speed bounded");

            // 00:00 -> 23:00 must move backwards by one hour, not almost a full day.
            var beforeMidnight = WorldClockSmoothingPolicy.AdvancePhase(0f, 23f / 24f, 0.25f, 12f);
            Assert(beforeMidnight < 1f && beforeMidnight > 0.99f, "00:00 should move backwards across midnight");

            var halfway = WorldClockSmoothingPolicy.AdvancePhase(0f, 0.5f, 1f, 90f);
            Assert(Math.Abs(halfway - 0.25f) < 0.00001f, "advance is bounded to 90 degrees per second");
            var arrived = WorldClockSmoothingPolicy.AdvancePhase(0f, 0.25f, 10f, 12f);
            Assert(Math.Abs(arrived - 0.25f) < 0.00001f, "reaches target when enough time passes");

            var invalidDelta = WorldClockSmoothingPolicy.AdvancePhase(0.4f, 0.8f, float.NaN, 12f);
            Assert(Math.Abs(invalidDelta - 0.4f) < 0.00001f, "invalid delta does not move phase");
            var invalidSpeed = WorldClockSmoothingPolicy.AdvancePhase(0.4f, 0.8f, 1f, -1f);
            Assert(Math.Abs(invalidSpeed - 0.4f) < 0.00001f, "invalid speed does not move phase");
            var wrapped = WorldClockSmoothingPolicy.AdvancePhase(1.2f, 0.2f, 1f, 360f);
            Assert(Math.Abs(wrapped - 0.2f) < 0.00001f, "phase normalizes to one day cycle");

            Console.WriteLine("World clock smoothing: " + _checks + " checks passed");
            return 0;
        }
    }
}
