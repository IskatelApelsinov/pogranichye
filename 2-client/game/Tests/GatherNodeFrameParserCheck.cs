using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

internal static class GatherNodeFrameParserCheck
{
    private static int _assertions;
    private static int _failures;

    private static void Check(string name, bool condition)
    {
        _assertions++;
        if (condition) return;
        _failures++;
        Console.Error.WriteLine("FAIL: " + name);
    }

    public static int Main()
    {
        const string validJson = "{\"id\":\"g:0:0\",\"kind\":\"herb\",\"name\":\"Зверобой\",\"good\":\"herb\",\"good_name\":\"травы\",\"left\":3,\"max\":3,\"e\":40.2,\"n\":80.4,\"u\":1.2,\"x\":12.3,\"y\":34.5,\"dist\":2.5,\"dir\":\"северо-восток\",\"here\":true}";
        var valid = JObject.Parse(validJson);
        var normalized = GatherNodeFrameParser.Normalize(new JArray(valid));
        Check("valid v1.30 node is retained", normalized.Count == 1);
        var typed = normalized.Count == 1 ? normalized[0].ToObject<GatherNodeFrame>() : null;
        Check("typed DTO maps stable ID, localized name and stock",
            typed != null && typed.Id == "g:0:0" && typed.Name == "Зверобой" &&
            typed.GoodName == "травы" && typed.Remaining == 3d && typed.Maximum == 3d);
        Check("typed DTO preserves server proximity and world position",
            typed != null && typed.Here && typed.DistanceMeters == 2.5d &&
            typed.East == 40.2d && typed.North == 80.4d && typed.Up == 1.2d);

        Check("missing field is backward-compatible",
            GatherNodeFrameParser.Normalize(null).Count == 0);
        Check("null field is backward-compatible",
            GatherNodeFrameParser.Normalize(JValue.CreateNull()).Count == 0);
        Check("malformed top-level field is hidden",
            GatherNodeFrameParser.Normalize(JObject.Parse(validJson)).Count == 0);

        var mixed = new JArray(JValue.CreateNull(), new JObject(), valid.DeepClone());
        Check("malformed entries do not hide a valid sibling",
            GatherNodeFrameParser.Normalize(mixed).Count == 1);

        var duplicate = new JObject(valid);
        duplicate["name"] = "Дубликат";
        Check("duplicate stable IDs are emitted only once",
            GatherNodeFrameParser.Normalize(new JArray(valid.DeepClone(), duplicate)).Count == 1);

        var negativeDistance = new JObject(valid);
        negativeDistance["dist"] = -1d;
        Check("negative distance is discarded",
            GatherNodeFrameParser.Normalize(new JArray(negativeDistance)).Count == 0);

        var impossibleStock = new JObject(valid);
        impossibleStock["left"] = 4d;
        Check("remaining stock cannot exceed maximum",
            GatherNodeFrameParser.Normalize(new JArray(impossibleStock)).Count == 0);

        var nonFinite = new JObject(valid);
        nonFinite["e"] = double.NaN;
        Check("non-finite world coordinates are discarded",
            GatherNodeFrameParser.Normalize(new JArray(nonFinite)).Count == 0);

        var unknownFutureKind = new JObject(valid);
        unknownFutureKind["kind"] = "future_kind";
        Check("unknown future kinds remain displayable",
            GatherNodeFrameParser.Normalize(new JArray(unknownFutureKind)).Count == 1);

        var invalidHere = new JObject(valid);
        invalidHere["here"] = "true";
        Check("proximity must remain server-authored boolean",
            GatherNodeFrameParser.Normalize(new JArray(invalidHere)).Count == 0);

        Console.WriteLine(_failures == 0
            ? "GatherNodeFrameParser: " + _assertions + " assertions passed"
            : "GatherNodeFrameParser: " + _failures + " of " + _assertions + " assertions failed");
        return _failures == 0 ? 0 : 1;
    }
}
