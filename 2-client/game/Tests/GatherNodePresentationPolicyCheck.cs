using System;
using Pogranichye.Client;

internal static class GatherNodePresentationPolicyCheck
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        Require(GatherNodePresentationPolicy.IsValid("g:-2:4", "herb", 44.9, 2),
            "valid server node must be visible");
        Require(!GatherNodePresentationPolicy.IsValid("", "herb", 1, 1),
            "empty id must be rejected");
        Require(!GatherNodePresentationPolicy.IsValid("site:1", "herb", 1, 1),
            "non-gather id must be rejected");
        Require(!GatherNodePresentationPolicy.IsValid("g:1:2", "", 1, 1),
            "empty kind must be rejected");
        Require(!GatherNodePresentationPolicy.IsValid("g:1:2", "herb", double.NaN, 1),
            "NaN distance must be rejected");
        Require(!GatherNodePresentationPolicy.IsValid("g:1:2", "herb", double.PositiveInfinity, 1),
            "infinite distance must be rejected");
        Require(!GatherNodePresentationPolicy.IsValid("g:1:2", "herb", -0.1, 1),
            "negative distance must be rejected");
        Require(!GatherNodePresentationPolicy.IsValid("g:1:2", "herb", 1, -1),
            "negative stock must be rejected");

        Require(GatherNodePresentationPolicy.CanPick(true, 3.0, 1),
            "server-confirmed in-reach node with stock must be pickable");
        Require(!GatherNodePresentationPolicy.CanPick(false, 1, 1),
            "client must not override server here=false");
        Require(!GatherNodePresentationPolicy.CanPick(true, 3.01, 1),
            "node outside the server's 3m reach must not offer pick");
        Require(!GatherNodePresentationPolicy.CanPick(true, 1, 0),
            "empty node must not offer pick");
        Require(!GatherNodePresentationPolicy.CanPick(true, double.NaN, 1),
            "non-finite distance must not offer pick");

        Require(GatherNodePresentationPolicy.KindName("hive") == "дикий улей",
            "hive label must be recognizable");
        Require(GatherNodePresentationPolicy.KindName("unknown") == "находка",
            "future kinds must fail soft");

        Console.WriteLine("PASS: server gather-node visibility and pick affordance policy");
    }
}
