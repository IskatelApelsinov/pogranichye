using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking;

internal static class ActionResultFrameParserCheck
{
    private static int _failures;

    private static void Check(string name, bool condition)
    {
        if (condition) return;
        Console.Error.WriteLine("FAIL: " + name);
        _failures++;
    }

    private static void ExpectFormat(string name, Action action)
    {
        try { action(); Check(name, false); }
        catch (FormatException) { Check(name, true); }
    }

    public static int Main()
    {
        var result = ActionResultFrameParser.Parse(
            JToken.Parse("{\"reqid\":\"abc-123\",\"ok\":false,\"reason\":\"нужно подойти\"}"), "me.result");
        Check("maps reqid/ok/reason", result != null && result.RequestId == "abc-123" &&
            !result.Ok && result.Reason == "нужно подойти");
        Check("absent result is accepted", ActionResultFrameParser.Parse(null, "me.result") == null);
        Check("explicit null result is accepted",
            ActionResultFrameParser.Parse(JValue.CreateNull(), "me.result") == null);

        ExpectFormat("non-object result rejected",
            () => ActionResultFrameParser.Validate(JToken.Parse("[]"), "me.result"));
        ExpectFormat("empty reqid rejected",
            () => ActionResultFrameParser.Validate(JToken.Parse("{\"reqid\":\"\",\"ok\":true,\"reason\":\"\"}"), "me.result"));
        ExpectFormat("non-string reqid rejected",
            () => ActionResultFrameParser.Validate(JToken.Parse("{\"reqid\":7,\"ok\":true,\"reason\":\"\"}"), "me.result"));
        ExpectFormat("missing ok rejected",
            () => ActionResultFrameParser.Validate(JToken.Parse("{\"reqid\":\"x\",\"reason\":\"\"}"), "me.result"));
        ExpectFormat("non-boolean ok rejected",
            () => ActionResultFrameParser.Validate(JToken.Parse("{\"reqid\":\"x\",\"ok\":1,\"reason\":\"\"}"), "me.result"));
        ExpectFormat("missing reason rejected",
            () => ActionResultFrameParser.Validate(JToken.Parse("{\"reqid\":\"x\",\"ok\":true}"), "me.result"));
        ExpectFormat("non-string reason rejected",
            () => ActionResultFrameParser.Validate(JToken.Parse("{\"reqid\":\"x\",\"ok\":true,\"reason\":false}"), "me.result"));

        FastFramePatch patch;
        Check("fast result parsed", FastFrameParser.TryParse(
            "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"result\":{\"reqid\":\"fast-1\",\"ok\":true,\"reason\":\"\"}}}", out patch) &&
            patch != null && patch.HasResult && patch.Result != null && patch.Result.RequestId == "fast-1" && patch.Result.Ok);
        Check("fast result omitted is distinct", FastFrameParser.TryParse(
            "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"health\":90}}", out patch) &&
            patch != null && !patch.HasResult && patch.Result == null);
        Check("fast result null is distinct", FastFrameParser.TryParse(
            "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"result\":null}}", out patch) &&
            patch != null && patch.HasResult && patch.Result == null);
        ExpectFormat("malformed fast result rejected",
            () => FastFrameParser.TryParse(
                "{\"v\":1,\"fast\":true,\"alive\":true,\"me\":{\"result\":{\"reqid\":\"x\",\"ok\":true}}}", out patch));

        Console.WriteLine(_failures == 0
            ? "ActionResultFrameParser: all checks passed"
            : "ActionResultFrameParser: FAILED " + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
