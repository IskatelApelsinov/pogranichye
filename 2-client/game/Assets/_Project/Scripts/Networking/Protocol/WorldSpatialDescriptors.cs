using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Pogranichye.Client.Networking.Protocol
{
    // Optional additive descriptors for server-authored terrain presentation.
    // Coordinates in Terrain* feature records are metres relative to the chunk origin.
    [Serializable]
    public sealed class TerrainCoverFrame
    {
        [JsonProperty("forest")] public double Forest { get; set; }
        [JsonProperty("grass")] public double Grass { get; set; }
        [JsonProperty("rock")] public double Rock { get; set; }
        [JsonProperty("wet")] public double Wet { get; set; }
    }

    [Serializable]
    public sealed class TerrainWaterFeatureFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        // river | stream | lake | pond | coast | waterfall
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("width_m")] public double WidthMeters { get; set; }
        [JsonProperty("water_level_m")] public double WaterLevelMeters { get; set; }
        [JsonProperty("flow")] public double Flow { get; set; }
        [JsonProperty("points")] public List<TerrainFeaturePointFrame> Points { get; set; }
    }

    [Serializable]
    public sealed class TerrainPathFeatureFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        // road | trail | bridge | pass
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("width_m")] public double WidthMeters { get; set; }
        [JsonProperty("points")] public List<TerrainFeaturePointFrame> Points { get; set; }
    }

    [Serializable]
    public sealed class TerrainFeaturePointFrame
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
        [JsonProperty("ground_m")] public double? GroundMeters { get; set; }
        [JsonProperty("surface_m")] public double? SurfaceMeters { get; set; }
    }

    [Serializable]
    public sealed class TerrainResourceHintFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
        [JsonProperty("count")] public int Count { get; set; }
    }

    [Serializable]
    public sealed class TerrainWildHintFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
        [JsonProperty("radius_m")] public double RadiusMeters { get; set; }
    }

    // A space is a server-authored semantic/structural volume, not a Unity scene.
    // origin/bounds/rooms/portals use global E/N/U metres; surface/stair points are local to
    // space.origin with x=East, y=Up, z=North. Client geometry never grants movement authority.
    [Serializable]
    public sealed class WorldSpaceChunkFrame
    {
        [JsonProperty("scheme")] public string Scheme { get; set; }
        [JsonProperty("v")] public int Version { get; set; }
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("chunk")] public long[] Chunk { get; set; }
        [JsonProperty("revision")] public long Revision { get; set; }
        [JsonProperty("spaces")] public List<WorldSpaceFrame> Spaces { get; set; }
        public bool IsValid => Scheme == "yof-tangent" && Version == 1 && Frame >= 0 &&
            Chunk != null && Chunk.Length == 2 && Spaces != null;
    }

    [Serializable]
    public sealed class WorldSpaceFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        // building | floor | cave | mine | cellar | basement | tunnel
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("parent_id")] public string ParentId { get; set; }
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("origin")] public WorldSpaceOriginFrame Origin { get; set; }
        [JsonProperty("bounds")] public WorldSpaceBoundsFrame Bounds { get; set; }
        [JsonProperty("floors")] public List<WorldSpaceFloorFrame> Floors { get; set; }
        [JsonProperty("surfaces")] public List<WorldSpaceSurfaceFrame> Surfaces { get; set; }
        [JsonProperty("rooms")] public List<WorldSpaceRoomFrame> Rooms { get; set; }
        [JsonProperty("portals")] public List<WorldSpacePortalFrame> Portals { get; set; }
        [JsonProperty("stairs")] public List<WorldSpaceStairFrame> Stairs { get; set; }
    }

    [Serializable]
    public sealed class WorldSpaceOriginFrame
    {
        [JsonProperty("e")] public double East { get; set; }
        [JsonProperty("n")] public double North { get; set; }
        [JsonProperty("u")] public double Up { get; set; }
    }

    [Serializable]
    public sealed class WorldSpaceBoundsFrame
    {
        [JsonProperty("min_e")] public double MinEast { get; set; }
        [JsonProperty("min_n")] public double MinNorth { get; set; }
        [JsonProperty("min_u")] public double MinUp { get; set; }
        [JsonProperty("max_e")] public double MaxEast { get; set; }
        [JsonProperty("max_n")] public double MaxNorth { get; set; }
        [JsonProperty("max_u")] public double MaxUp { get; set; }
    }

    [Serializable]
    public sealed class WorldSpaceFloorFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("elevation_m")] public double ElevationMeters { get; set; }
        [JsonProperty("walkable")] public bool Walkable { get; set; }
    }

    [Serializable]
    public sealed class WorldSpaceSurfaceFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("floor_id")] public string FloorId { get; set; }
        // floor | ceiling | wall | roof | cliff | cave_wall | waterbed | window
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("points")] public List<WorldSpacePointFrame> Points { get; set; }
        [JsonProperty("collidable")] public bool Collidable { get; set; }
        [JsonProperty("walkable")] public bool Walkable { get; set; }
        // Optional visual transparency; it never changes the server-authored collider.
        [JsonProperty("transparent")] public bool Transparent { get; set; }
    }

    [Serializable]
    public sealed class WorldSpaceRoomFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("floor_id")] public string FloorId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("bounds")] public WorldSpaceBoundsFrame Bounds { get; set; }
    }

    [Serializable]
    public sealed class WorldSpacePortalFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("target_space_id")] public string TargetSpaceId { get; set; }
        [JsonProperty("target_portal_id")] public string TargetPortalId { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("e")] public double East { get; set; }
        [JsonProperty("n")] public double North { get; set; }
        [JsonProperty("u")] public double Up { get; set; }
        [JsonProperty("width_m")] public double WidthMeters { get; set; }
        [JsonProperty("height_m")] public double HeightMeters { get; set; }
        [JsonProperty("open")] public bool Open { get; set; }
    }

    [Serializable]
    public sealed class WorldSpaceStairFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("from_floor")] public string FromFloor { get; set; }
        [JsonProperty("to_floor")] public string ToFloor { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("width_m")] public double WidthMeters { get; set; }
        [JsonProperty("step_count")] public int StepCount { get; set; }
        [JsonProperty("points")] public List<WorldSpacePointFrame> Points { get; set; }
    }

    [Serializable]
    public sealed class WorldSpacePointFrame
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
    }
}
