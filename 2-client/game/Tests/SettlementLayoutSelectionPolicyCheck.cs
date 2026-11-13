using System;
using Pogranichye.Client.Presentation;

internal static class SettlementLayoutSelectionPolicyCheck
{
    private static int _failures;

    private static void Check(string name, bool condition)
    {
        if (condition) return;
        Console.Error.WriteLine("FAIL: " + name);
        _failures++;
    }

    public static int Main()
    {
        Check("explicit starter layout is tutorial", SettlementLayoutSelectionPolicy.IsTutorialLayout("starter"));
        Check("legacy default layout without plan is tutorial", SettlementLayoutSelectionPolicy.IsTutorialLayout(null));
        Check("generated layout is not tutorial", !SettlementLayoutSelectionPolicy.IsTutorialLayout("cross"));

        Check("explicit starter place is selected after another settlement",
            SettlementLayoutSelectionPolicy.IsTutorialSettlement("starter", "village-a", "village-a", true));
        Check("first legacy snapshot without plan uses default layout",
            SettlementLayoutSelectionPolicy.IsTutorialSettlement(null, "village-a", null, false));
        Check("known tutorial place is restored after generated settlement",
            SettlementLayoutSelectionPolicy.IsTutorialSettlement(null, "village-a", "village-a", true));
        Check("unknown legacy place is not mistaken for tutorial",
            !SettlementLayoutSelectionPolicy.IsTutorialSettlement(null, "village-b", "village-a", true));
        Check("explicit generated plan overrides a stale tutorial ID",
            !SettlementLayoutSelectionPolicy.IsTutorialSettlement("cross", "village-a", "village-a", true));

        var tutorial = new object();
        var generated = new object();
        Check("tutorial selection restores default Layout v2",
            ReferenceEquals(tutorial, SettlementLayoutSelectionPolicy.SelectForSettlement(true, tutorial, generated)));
        Check("generated selection keeps its own layout",
            ReferenceEquals(generated, SettlementLayoutSelectionPolicy.SelectForSettlement(false, tutorial, generated)));
        Check("missing tutorial layout never reuses stale generated geometry",
            SettlementLayoutSelectionPolicy.SelectForSettlement(true, null, generated) == null);

        if (_failures == 0)
            Console.WriteLine("SettlementLayoutSelectionPolicy: 11 checks passed");
        return _failures == 0 ? 0 : 1;
    }
}
