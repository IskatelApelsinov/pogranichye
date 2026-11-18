using System;
using Pogranichye.Client.Presentation;

internal static class WorldRoomAcousticPolicyCheck
{
    private static int _checks;
    private static void Check(string name, bool condition)
    {
        _checks++;
        if (!condition) throw new Exception(name);
    }

    public static void Main()
    {
        Check("cave uses cave reverb", WorldRoomAcousticPolicy.UsesCaveReverb("cave"));
        Check("tunnel uses cave reverb", WorldRoomAcousticPolicy.UsesCaveReverb("tunnel"));
        Check("tavern uses room reverb", !WorldRoomAcousticPolicy.UsesCaveReverb("tavern"));
        Check("cellar uses room reverb", !WorldRoomAcousticPolicy.UsesCaveReverb("cellar"));
        Check("tower uses room reverb", !WorldRoomAcousticPolicy.UsesCaveReverb("tower"));
        Check("unknown type does not partially match cave", !WorldRoomAcousticPolicy.UsesCaveReverb("underground_cave"));
        Check("uncontracted stone label does not partially match", !WorldRoomAcousticPolicy.UsesCaveReverb("stone"));
        Check("missing kind remains neutral", !WorldRoomAcousticPolicy.UsesCaveReverb(null));
        Console.WriteLine("World room acoustic policy: " + _checks + " checks passed.");
    }
}
