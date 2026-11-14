using System;
using Pogranichye.Client;

internal static class GameplayStatusViewCheck
{
    private static int _failures;

    private static void Check(string name, bool condition)
    {
        if (condition) return;
        Console.Error.WriteLine("FAIL: " + name);
        _failures++;
    }

    private static bool Near(float actual, float expected)
    {
        return Math.Abs(actual - expected) < 0.0001f;
    }

    public static int Main()
    {
        var full = GameplayStatusView.From(92d, 67d, 31d, 18d, 0.8d);
        Check("full snapshot maps health", Near(full.Health, 92f));
        Check("full snapshot maps energy", Near(full.Energy, 67f));
        Check("full snapshot maps mana", Near(full.Mana, 31f));
        Check("full snapshot maps drunk", Near(full.Drunk, 18f));
        Check("full snapshot maps stamina", Near(full.Stamina, 0.8f));

        var partial = full.Apply(new GameplayStatusPatch(
            true, 48f,
            false, 0f,
            true, 22f,
            true, 0.35f));
        Check("fast health replaces the previous value", Near(partial.Health, 48f));
        Check("omitted fast energy preserves the full value", Near(partial.Energy, 67f));
        Check("fast mana replaces the previous value", Near(partial.Mana, 22f));
        Check("fast frame does not contain or erase drunk", Near(partial.Drunk, 18f));
        Check("fast position stamina replaces the previous value", Near(partial.Stamina, 0.35f));

        var explicitNulls = partial.Apply(new GameplayStatusPatch(
            true, 0f,
            false, 0f,
            false, 0f,
            true, 0f));
        Check("explicit null numeric field clears to legacy zero", Near(explicitNulls.Health, 0f));
        Check("explicit null position clears stamina to zero", Near(explicitNulls.Stamina, 0f));
        Check("other omitted fields remain unchanged", Near(explicitNulls.Mana, 22f) && Near(explicitNulls.Drunk, 18f));
        Check("patching does not mutate the previous view", Near(partial.Health, 48f) && Near(partial.Stamina, 0.35f));
        Check("null patch is a no-op", ReferenceEquals(partial, partial.Apply(null)));

        if (_failures == 0)
        {
            Console.WriteLine("GameplayStatusView checks passed.");
            return 0;
        }

        Console.Error.WriteLine(_failures + " GameplayStatusView check(s) failed.");
        return 1;
    }
}
