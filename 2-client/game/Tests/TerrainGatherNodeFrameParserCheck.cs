using System;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

internal static class TerrainGatherNodeFrameParserCheck
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
        const string validJson = "{\"id\":\"g:-2:4\",\"kind\":\"herb\",\"good\":\"herb\",\"x\":12.3,\"z\":34.5,\"left\":3}";
        var valid = JObject.Parse(validJson);
        var normalized = TerrainGatherNodeFrameParser.Normalize(new JArray(valid));
        Check("valid terrain node is retained", normalized.Count == 1);
        var typed = normalized.Count == 1 ? normalized[0].ToObject<TerrainGatherNodeFrame>() : null;
        Check("typed DTO preserves stable ID and kind",
            typed != null && typed.Id == "g:-2:4" && typed.Kind == "herb" && typed.Good == "herb");
        Check("typed DTO preserves chunk-local coordinates and stock",
            typed != null && Math.Abs(typed.X - 12.3d) < 0.0001d &&
            Math.Abs(typed.Z - 34.5d) < 0.0001d && typed.Remaining == 3d);

        Check("missing field is backward-compatible",
            TerrainGatherNodeFrameParser.Normalize(null).Count == 0);
        Check("null field is backward-compatible",
            TerrainGatherNodeFrameParser.Normalize(JValue.CreateNull()).Count == 0);
        Check("wrong top-level type is ignored",
            TerrainGatherNodeFrameParser.Normalize(JObject.Parse(validJson)).Count == 0);

        var mixed = new JArray(JValue.CreateNull(), new JObject(), valid.DeepClone());
        Check("malformed siblings do not hide a valid node",
            TerrainGatherNodeFrameParser.Normalize(mixed).Count == 1);

        var duplicate = new JObject(valid);
        duplicate["left"] = 1;
        Check("duplicate stable IDs are emitted once",
            TerrainGatherNodeFrameParser.Normalize(new JArray(valid.DeepClone(), duplicate)).Count == 1);

        var badId = new JObject(valid);
        badId["id"] = "site:1";
        Check("non-gather IDs are discarded",
            TerrainGatherNodeFrameParser.Normalize(new JArray(badId)).Count == 0);

        var outsideX = new JObject(valid);
        outsideX["x"] = 256d;
        Check("east coordinate must be inside the chunk",
            TerrainGatherNodeFrameParser.Normalize(new JArray(outsideX)).Count == 0);

        var outsideZ = new JObject(valid);
        outsideZ["z"] = -0.01d;
        Check("north coordinate cannot be negative",
            TerrainGatherNodeFrameParser.Normalize(new JArray(outsideZ)).Count == 0);

        var negativeStock = new JObject(valid);
        negativeStock["left"] = -1d;
        Check("negative stock is discarded",
            TerrainGatherNodeFrameParser.Normalize(new JArray(negativeStock)).Count == 0);

        var nonFinite = new JObject(valid);
        nonFinite["x"] = double.NaN;
        Check("non-finite coordinates are discarded",
            TerrainGatherNodeFrameParser.Normalize(new JArray(nonFinite)).Count == 0);

        var stringCoordinate = new JObject(valid);
        stringCoordinate["z"] = "34.5";
        Check("coordinates must remain numeric",
            TerrainGatherNodeFrameParser.Normalize(new JArray(stringCoordinate)).Count == 0);

        var unknownKind = new JObject(valid);
        unknownKind["kind"] = "future_kind";
        Check("unknown future kinds remain parseable",
            TerrainGatherNodeFrameParser.Normalize(new JArray(unknownKind)).Count == 1);

        var missingGood = new JObject(valid);
        missingGood.Remove("good");
        Check("incomplete node descriptors are discarded",
            TerrainGatherNodeFrameParser.Normalize(new JArray(missingGood)).Count == 0);

        Console.WriteLine(_failures == 0
            ? "TerrainGatherNodeFrameParser: " + _assertions + " assertions passed"
            : "TerrainGatherNodeFrameParser: " + _failures + " of " + _assertions + " assertions failed");
        return _failures == 0 ? 0 : 1;
    }
}
