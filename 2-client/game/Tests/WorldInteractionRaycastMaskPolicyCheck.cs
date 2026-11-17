using System;
using Pogranichye.Client.Presentation;

internal static class WorldInteractionRaycastMaskPolicyCheck
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
        var ignoreRaycastBit = 1 << WorldInteractionRaycastMaskPolicy.IgnoreRaycastLayer;
        var defaultRaycastMask = ~ignoreRaycastBit;
        var interactionMask = WorldInteractionRaycastMaskPolicy.IncludeIgnoreRaycastLayer(defaultRaycastMask);

        Check("default raycast mask excludes Unity Ignore Raycast layer",
            (defaultRaycastMask & ignoreRaycastBit) == 0);
        Check("interaction mask includes solid building occluders",
            (interactionMask & ignoreRaycastBit) != 0);
        Check("all default raycast layers are preserved",
            (interactionMask & defaultRaycastMask) == defaultRaycastMask);
        Check("only the intentional occlusion layer is added",
            (interactionMask ^ defaultRaycastMask) == ignoreRaycastBit);
        Check("all-layers input remains unchanged",
            WorldInteractionRaycastMaskPolicy.IncludeIgnoreRaycastLayer(-1) == -1);

        Console.WriteLine("WorldInteractionRaycastMaskPolicy: " + _checks + " checks passed.");
        return 0;
    }
}
