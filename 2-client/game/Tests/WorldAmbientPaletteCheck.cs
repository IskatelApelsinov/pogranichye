using System;
namespace Pogranichye.Client.Presentation.Tests
{
    internal static class WorldAmbientPaletteCheck
    {
        private static int _checks;
        private static void Assert(bool condition, string message)
        {
            _checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static void CheckSample(WorldAmbientSample sample, string name)
        {
            var values = new[] { sample.Sky.R, sample.Sky.G, sample.Sky.B,
                sample.Horizon.R, sample.Horizon.G, sample.Horizon.B,
                sample.Ground.R, sample.Ground.G, sample.Ground.B };
            for (var i = 0; i < values.Length; i++)
                Assert(Finite(values[i]) && values[i] >= 0f && values[i] <= 1f, name + " channel " + i);
        }
        public static int Main()
        {
            var day = WorldAmbientPalette.Evaluate(1f, 0f);
            var night = WorldAmbientPalette.Evaluate(0f, 0f);
            var dusk = WorldAmbientPalette.Evaluate(0.5f, 1f);
            CheckSample(day, "day"); CheckSample(night, "night"); CheckSample(dusk, "dusk");
            Assert(day.Sky.R > day.Horizon.R && day.Horizon.R > day.Ground.R, "day gradient");
            Assert(night.Sky.B > night.Horizon.B && night.Horizon.B > night.Ground.B, "night gradient");
            Assert(night.Sky.R < day.Sky.R && night.Horizon.G < day.Horizon.G, "night darker than day");
            Assert(dusk.Sky.R > WorldAmbientPalette.Evaluate(0.5f, 0f).Sky.R, "warm dusk");
            var low = WorldAmbientPalette.Evaluate(-2f, -1f);
            var lowRef = WorldAmbientPalette.Evaluate(0f, 0f);
            Assert(Math.Abs(low.Sky.R - lowRef.Sky.R) < 0.00001f, "negative clamp");
            var high = WorldAmbientPalette.Evaluate(2f, 2f);
            var highRef = WorldAmbientPalette.Evaluate(1f, 1f);
            Assert(Math.Abs(high.Ground.G - highRef.Ground.G) < 0.00001f, "upper clamp");
            Console.WriteLine("World ambient palette: " + _checks + " checks passed");
            return 0;
        }
    }
}
