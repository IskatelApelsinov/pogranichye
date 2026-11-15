using System;
using Pogranichye.Client.Presentation;

internal static class WorldLayoutPoiInteractionPolicyCheck
{
    private static int _checks;

    private static void Check(string name, bool condition)
    {
        _checks++;
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("ok: " + name);
    }

    public static int Main()
    {
        WorldLayoutPoiInteraction interaction;
        Check("tavern maps to existing socialize goal",
            WorldLayoutPoiInteractionPolicy.TryResolve("tavern", out interaction) &&
            interaction.Goal == "socialize" && interaction.ActionIntent == null);
        Check("smithy maps to server forge action on E",
            WorldLayoutPoiInteractionPolicy.TryResolve("smithy", out interaction) &&
            interaction.Goal == null && interaction.ActionIntent == "forge");
        Check("market maps to existing eat goal",
            WorldLayoutPoiInteractionPolicy.TryResolve("market", out interaction) &&
            interaction.Goal == "eat" && interaction.ActionIntent == null);
        Check("garden maps to server gather action on E",
            WorldLayoutPoiInteractionPolicy.TryResolve("garden", out interaction) &&
            interaction.Goal == null && interaction.ActionIntent == "gather");
        Check("woods maps to existing chop action",
            WorldLayoutPoiInteractionPolicy.TryResolve("woods", out interaction) &&
            interaction.Goal == null && interaction.ActionIntent == "chop");
        Check("river maps to existing fish action",
            WorldLayoutPoiInteractionPolicy.TryResolve("river", out interaction) &&
            interaction.Goal == null && interaction.ActionIntent == "fish");
        Check("mine maps to existing mine action",
            WorldLayoutPoiInteractionPolicy.TryResolve("mine", out interaction) &&
            interaction.Goal == null && interaction.ActionIntent == "mine");
        Check("unsupported stable is not interactive",
            !WorldLayoutPoiInteractionPolicy.TryResolve("stable", out interaction));
        Check("unsupported granary is not interactive",
            !WorldLayoutPoiInteractionPolicy.TryResolve("granary", out interaction));
        Check("unsupported apiary is not interactive",
            !WorldLayoutPoiInteractionPolicy.TryResolve("apiary", out interaction));
        Check("unknown and null POIs fail closed",
            !WorldLayoutPoiInteractionPolicy.TryResolve("unknown", out interaction) &&
            !WorldLayoutPoiInteractionPolicy.TryResolve(null, out interaction));
        Check("allow-list is deterministic and bounded",
            WorldLayoutPoiInteractionPolicy.SupportedIds.Length == 7);
        Console.WriteLine("WorldLayoutPoiInteractionPolicy: " + _checks + " checks passed.");
        return 0;
    }
}
