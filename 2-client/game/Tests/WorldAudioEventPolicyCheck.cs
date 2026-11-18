using Pogranichye.Client.Presentation;

internal static class WorldAudioEventPolicyCheck
{
    private static int _failures;
    private static void Check(string name, bool condition)
    {
        if (condition) return;
        System.Console.Error.WriteLine("FAIL: " + name);
        _failures++;
    }

    public static int Main()
    {
        Check("real resolved movement plays footsteps", WorldAudioEventPolicy.ShouldPlayFootstep(0.01f));
        Check("blocked movement stays silent", !WorldAudioEventPolicy.ShouldPlayFootstep(0f));
        Check("tiny correction noise stays silent", !WorldAudioEventPolicy.ShouldPlayFootstep(0.0001f));
        Check("invalid movement distance stays silent", !WorldAudioEventPolicy.ShouldPlayFootstep(float.NaN));
        Check("confirmed weave plays magic", WorldAudioEventPolicy.ShouldPlayMagic("Плетение", true));
        Check("rejected weave is silent", !WorldAudioEventPolicy.ShouldPlayMagic("Плетение", false));
        Check("confirmed cast plays magic", WorldAudioEventPolicy.ShouldPlayMagic("Действие мира: cast", true));
        Check("confirmed explicit weave plays magic", WorldAudioEventPolicy.ShouldPlayMagic("Действие мира: weave", true));
        Check("unrelated accepted action is silent", !WorldAudioEventPolicy.ShouldPlayMagic("Действие мира: chop", true));
        Check("empty label is silent", !WorldAudioEventPolicy.ShouldPlayMagic("", true));
        Check("null label is silent", !WorldAudioEventPolicy.ShouldPlayMagic(null, true));
        Check("unrecognized label is not guessed", !WorldAudioEventPolicy.ShouldPlayMagic("Магическое действие", true));
        System.Console.WriteLine(_failures == 0 ? "WorldAudioEventPolicy: all checks passed" : "WorldAudioEventPolicy: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
