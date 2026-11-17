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
        Check("real forest maps to existing chop action",
            WorldLayoutPoiInteractionPolicy.TryResolve("woods", "forest", out interaction) &&
            interaction.Title == "Лес" && interaction.Goal == null && interaction.ActionIntent == "chop");
        Check("copse maps to brushwood, not timber chopping",
            WorldLayoutPoiInteractionPolicy.TryResolve("woods", "copse", out interaction) &&
            interaction.Title == "Рощица · хворост" && interaction.ActionIntent == "gather_brushwood");
        Check("woods without a matching authoritative zone fails closed",
            !WorldLayoutPoiInteractionPolicy.TryResolve("woods", out interaction) &&
            !WorldLayoutPoiInteractionPolicy.TryResolve("woods", "meadow", out interaction));
        Check("river maps to existing fish action",
            WorldLayoutPoiInteractionPolicy.TryResolve("river", out interaction) &&
            interaction.Goal == null && interaction.ActionIntent == "fish");
        Check("mine maps to existing mine action",
            WorldLayoutPoiInteractionPolicy.TryResolve("mine", out interaction) &&
            interaction.Goal == null && interaction.ActionIntent == "mine");
        Check("unsupported stable is not interactive",
            !WorldLayoutPoiInteractionPolicy.TryResolve("stable", out interaction));
        Check("granary opens the stable/workshop context without inventing a server action",
            WorldLayoutPoiInteractionPolicy.TryResolve("granary", out interaction) &&
            interaction.Goal == null && interaction.ActionIntent == null && interaction.TargetId == "granary");
        Check("hearth opens the recipe context without inventing a server action",
            WorldLayoutPoiInteractionPolicy.TryResolve("hearth", out interaction) &&
            interaction.Goal == null && interaction.ActionIntent == null && interaction.TargetId == "hearth");
        Check("unsupported apiary is not interactive",
            !WorldLayoutPoiInteractionPolicy.TryResolve("apiary", out interaction));
        Check("unknown and null POIs fail closed",
            !WorldLayoutPoiInteractionPolicy.TryResolve("unknown", out interaction) &&
            !WorldLayoutPoiInteractionPolicy.TryResolve(null, out interaction));
        Check("allow-list is deterministic and bounded",
            WorldLayoutPoiInteractionPolicy.SupportedIds.Length == 9);
        Console.WriteLine("WorldLayoutPoiInteractionPolicy: " + _checks + " checks passed.");
        return 0;
    }
}
