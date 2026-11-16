using System;
using System.IO;
using Newtonsoft.Json;
using Pogranichye.Client.Networking;
using Pogranichye.Client.Networking.Protocol;

public static class WorldSpaceDescriptorValidatorCheck
{
    private static int _checks;

    public static int Main(string[] args)
    {
        if (args == null || args.Length != 1)
        {
            Console.Error.WriteLine("Usage: WorldSpaceDescriptorValidatorCheck <spaces-fixture.json>");
            return 2;
        }

        var json = File.ReadAllText(args[0]);
        var fixture = JsonConvert.DeserializeObject<WorldSpaceChunkFrame>(json);
        Require(WorldSpaceDescriptorValidator.IsValid(fixture), "the committed server fixture must be accepted");

        var empty = Clone(fixture);
        empty.Spaces.Clear();
        Require(WorldSpaceDescriptorValidator.IsValid(empty), "an empty chunk must remain valid");

        ExpectInvalid(fixture, "duplicate space id", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces.Add(Clone(value).Spaces[0]);
        });
        ExpectInvalid(fixture, "space frame mismatch", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Frame++;
        });
        ExpectInvalid(fixture, "degenerate space bounds", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Bounds.MaxEast = value.Spaces[0].Bounds.MinEast;
        });
        ExpectInvalid(fixture, "duplicate floor id", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Floors[1].Id = value.Spaces[0].Floors[0].Id;
        });
        ExpectInvalid(fixture, "floor outside vertical bounds", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Floors[0].ElevationMeters = value.Spaces[0].Bounds.MaxUp + 1d;
        });
        ExpectInvalid(fixture, "surface with fewer than three vertices", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Surfaces[0].Points.RemoveAt(2);
            value.Spaces[0].Surfaces[0].Points.RemoveAt(2);
        });
        ExpectInvalid(fixture, "duplicate surface id", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Surfaces[1].Id = value.Spaces[0].Surfaces[0].Id;
        });
        ExpectInvalid(fixture, "surface references an unknown floor", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Surfaces[0].FloorId = "missing-floor";
        });
        ExpectInvalid(fixture, "surface vertex outside global bounds", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Surfaces[0].Points[0].X = 1000d;
        });
        ExpectInvalid(fixture, "room references an unknown floor", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Rooms[0].FloorId = "missing-floor";
        });
        ExpectInvalid(fixture, "room outside space bounds", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Rooms[0].Bounds.MaxEast = value.Spaces[0].Bounds.MaxEast + 1d;
        });
        ExpectInvalid(fixture, "portal with zero width", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Portals[0].WidthMeters = 0d;
        });
        ExpectInvalid(fixture, "portal outside space bounds", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Portals[0].East = value.Spaces[0].Bounds.MaxEast + 1d;
        });
        ExpectInvalid(fixture, "stair references an unknown floor", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Stairs[0].ToFloor = "missing-floor";
        });
        ExpectInvalid(fixture, "stair with zero width", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Stairs[0].WidthMeters = 0d;
        });
        ExpectInvalid(fixture, "ordinary stair with zero steps", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Stairs[0].StepCount = 0;
        });
        ExpectInvalid(fixture, "stair point outside space bounds", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Stairs[0].Points[0].X = 1000d;
        });
        ExpectInvalid(fixture, "stair endpoint does not meet target floor", delegate(WorldSpaceChunkFrame value)
        {
            var points = value.Spaces[0].Stairs[0].Points;
            points[points.Count - 1].Y += 1d;
        });
        ExpectInvalid(fixture, "duplicate stair id", delegate(WorldSpaceChunkFrame value)
        {
            value.Spaces[0].Stairs[1].Id = value.Spaces[0].Stairs[0].Id;
        });

        Console.WriteLine("World-space descriptor validation: " + _checks + " checks passed.");
        return 0;
    }

    private static WorldSpaceChunkFrame Clone(WorldSpaceChunkFrame value)
    {
        return JsonConvert.DeserializeObject<WorldSpaceChunkFrame>(JsonConvert.SerializeObject(value));
    }

    private static void ExpectInvalid(WorldSpaceChunkFrame fixture, string name,
        Action<WorldSpaceChunkFrame> mutate)
    {
        var value = Clone(fixture);
        mutate(value);
        Require(!WorldSpaceDescriptorValidator.IsValid(value), name + " must be rejected");
    }

    private static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }
}
