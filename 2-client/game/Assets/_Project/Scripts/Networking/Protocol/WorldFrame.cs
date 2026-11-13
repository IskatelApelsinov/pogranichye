using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class WorldFrame
    {
        [JsonProperty("v")] public int Version { get; set; }
        [JsonProperty("alive")] public bool Alive { get; set; }
        [JsonProperty("msg")] public string Message { get; set; }
        [JsonProperty("day")] public int Day { get; set; }
        [JsonProperty("year")] public int Year { get; set; }
        [JsonProperty("season")] public string Season { get; set; }
        [JsonProperty("me")] public CharacterFrame Me { get; set; }
        [JsonProperty("place")] public PlaceFrame Place { get; set; }
    }

    [Serializable]
    public sealed class CharacterFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("surname")] public string Surname { get; set; }
        [JsonProperty("nick")] public string Nick { get; set; }
        [JsonProperty("trait")] public string Trait { get; set; }
        [JsonProperty("origin")] public string Origin { get; set; }
        [JsonProperty("skills")] public Dictionary<string, double> Skills { get; set; }
        [JsonProperty("desire")] public string Desire { get; set; }
        [JsonProperty("inv")] public InventoryFrame Inventory { get; set; }
        [JsonProperty("world")] public List<SettlementFrame> World { get; set; }
        [JsonProperty("known_places")] public List<KnownPlaceFrame> KnownPlaces { get; set; }
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("sex")] public string Sex { get; set; }
        [JsonProperty("age")] public double Age { get; set; }
        [JsonProperty("health")] public double Health { get; set; }
        [JsonProperty("hunger")] public double Hunger { get; set; }
        [JsonProperty("energy")] public double Energy { get; set; }
        [JsonProperty("morale")] public double Morale { get; set; }
        [JsonProperty("goal")] public string Goal { get; set; }
        [JsonProperty("act_code")] public string ActivityCode { get; set; }
        [JsonProperty("mug")] public bool Mug { get; set; }
        [JsonProperty("mana")] public double Mana { get; set; }
        [JsonProperty("talk")] public TalkFrame Talk { get; set; }
        [JsonProperty("invite_from")] public InviteFromFrame InviteFrom { get; set; }
        [JsonProperty("social_event")] public SocialEventFrame SocialEvent { get; set; }
        [JsonProperty("result")] public ActionResultFrame Result { get; set; }
        [JsonProperty("task")] public OpeningTaskFrame Task { get; set; }
        [JsonProperty("journal")] public List<JournalEntryFrame> Journal { get; set; }
        [JsonProperty("last")] public string Last { get; set; }
        [JsonProperty("home")] public string Home { get; set; }
        [JsonProperty("coins")] public double Coins { get; set; }
        [JsonProperty("debt")] public double Debt { get; set; }
        [JsonProperty("wounds")] public Dictionary<string, double> Wounds { get; set; }
        [JsonProperty("bleeding")] public double Bleeding { get; set; }
        [JsonProperty("stash")] public Dictionary<string, double> Stash { get; set; }
        [JsonProperty("pos")] public ScenePositionFrame Position { get; set; }
        // v1.19 canonical global position; null on older servers or while outside the world.
        [JsonProperty("wpos")] public WorldPositionFrame WorldPosition { get; set; }
    }

    [Serializable]
    public sealed class InventoryFrame
    {
        [JsonProperty("items")] public Dictionary<string, int> Items { get; set; }
        [JsonProperty("hand")] public string Hand { get; set; }
        [JsonProperty("worn")] public Dictionary<string, string> Worn { get; set; }
    }

    [Serializable]
    public sealed class SettlementFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("tier")] public string Tier { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("outer")] public bool Outer { get; set; }
    }

    [Serializable]
    public sealed class KnownPlaceFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("visited")] public bool Visited { get; set; }
        [JsonProperty("known")] public bool Known { get; set; }
    }

    [Serializable]
    public sealed class WorldPositionFrame
    {
        [JsonProperty("scheme")] public string Scheme { get; set; }
        [JsonProperty("v")] public int Version { get; set; }
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("e")] public double East { get; set; }
        [JsonProperty("n")] public double North { get; set; }
        [JsonProperty("u")] public double Up { get; set; }
        [JsonProperty("ground")] public double Ground { get; set; }
        [JsonProperty("z")] public double HeightAboveGround { get; set; }
        [JsonProperty("chunk")] public long[] Chunk { get; set; }
        [JsonProperty("layer")] public long Layer { get; set; }
        [JsonProperty("lat")] public double Latitude { get; set; }
        [JsonProperty("lon")] public double Longitude { get; set; }
        public bool IsValid => Scheme == "yof-tangent" && Version == 1 && Frame >= 0 && Chunk != null && Chunk.Length == 2 &&
            IsFinite(East) && IsFinite(North) && IsFinite(Up) && IsFinite(Ground) && IsFinite(HeightAboveGround);
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    [Serializable]
    public sealed class WorldAnchorFrame
    {
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("e")] public double East { get; set; }
        [JsonProperty("n")] public double North { get; set; }
        [JsonProperty("u")] public double Up { get; set; }
    }

    [Serializable]
    public sealed class ScenePositionFrame
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("facing")] public double Facing { get; set; }
        [JsonProperty("stam")] public double Stamina { get; set; }
        [JsonProperty("seq")] public long Sequence { get; set; }
    }

    [Serializable]
    public sealed class PlaceFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        /// <summary>v1.19: settlement plan ("starter", "street", "cross", "ring", "scatter").</summary>
        [JsonProperty("plan")] public string Plan { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("tier")] public string Tier { get; set; }
        [JsonProperty("food")] public double Food { get; set; }
        [JsonProperty("bread")] public double Bread { get; set; }
        [JsonProperty("bread_price")] public double BreadPrice { get; set; }
        [JsonProperty("tavern")] public bool Tavern { get; set; }
        [JsonProperty("beer")] public double Beer { get; set; }
        [JsonProperty("beer_price")] public double BeerPrice { get; set; }
        [JsonProperty("neighbors")] public List<NeighborFrame> Neighbors { get; set; }
        [JsonProperty("life")] public SettlementLifeFrame Life { get; set; }
        // v1.19 global anchor. Local pos remains settlement-relative.
        [JsonProperty("anchor")] public WorldAnchorFrame Anchor { get; set; }
    }

    [Serializable]
    public sealed class SettlementLifeFrame
    {
        [JsonProperty("population")] public int Population { get; set; }
        [JsonProperty("working")] public int Working { get; set; }
        [JsonProperty("activities")] public SettlementActivityCountsFrame Activities { get; set; }
        [JsonProperty("stock")] public SettlementStockFrame Stock { get; set; }
    }

    [Serializable]
    public sealed class SettlementActivityCountsFrame
    {
        [JsonProperty("field")] public int Field { get; set; }
        [JsonProperty("maintenance")] public int Maintenance { get; set; }
        [JsonProperty("wood")] public int Wood { get; set; }
        [JsonProperty("forge")] public int Forge { get; set; }
        [JsonProperty("mine")] public int Mine { get; set; }
        [JsonProperty("fish")] public int Fish { get; set; }
        [JsonProperty("hunt")] public int Hunt { get; set; }
        [JsonProperty("herb")] public int Herb { get; set; }
        [JsonProperty("cook")] public int Cook { get; set; }
        [JsonProperty("herd")] public int Herd { get; set; }
        [JsonProperty("magic")] public int Magic { get; set; }
        [JsonProperty("tavern")] public int Tavern { get; set; }
        [JsonProperty("rest")] public int Rest { get; set; }
        [JsonProperty("social")] public int Social { get; set; }
        [JsonProperty("eating")] public int Eating { get; set; }
        [JsonProperty("other")] public int Other { get; set; }
    }

    [Serializable]
    public sealed class SettlementStockFrame
    {
        [JsonProperty("wood")] public double Wood { get; set; }
        [JsonProperty("ore")] public double Ore { get; set; }
        [JsonProperty("metal")] public double Metal { get; set; }
        [JsonProperty("herb")] public double Herb { get; set; }
    }

    [Serializable]
    public sealed class NeighborFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("known")] public bool Known { get; set; }
        [JsonProperty("desc")] public string Description { get; set; }
        [JsonProperty("player")] public bool Controlled { get; set; }
        [JsonProperty("trait")] public string Trait { get; set; }
        [JsonProperty("want")] public string Want { get; set; }
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("act")] public string Act { get; set; }
        [JsonProperty("act_code")] public string ActivityCode { get; set; }
        [JsonProperty("pos")] public ScenePositionFrame Position { get; set; }
        [JsonProperty("gear")] public NeighborGearFrame Gear { get; set; }
    }

    [Serializable]
    public sealed class NeighborGearFrame
    {
        [JsonProperty("hand")] public string Hand { get; set; }
        [JsonProperty("worn")] public Dictionary<string, string> Worn { get; set; }
    }

    [Serializable]
    public sealed class ActionResultFrame
    {
        [JsonProperty("reqid")] public string RequestId { get; set; }
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("reason")] public string Reason { get; set; }
    }

    [Serializable]
    public sealed class IssuedErrandFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("giver")] public string Giver { get; set; }
        [JsonProperty("giver_name")] public string GiverName { get; set; }
        [JsonProperty("good")] public string Good { get; set; }
        [JsonProperty("qty")] public int Quantity { get; set; }
        [JsonProperty("have")] public int Have { get; set; }
        [JsonProperty("ready")] public bool Ready { get; set; }
        [JsonProperty("coins")] public double Coins { get; set; }
        [JsonProperty("accepted")] public bool Accepted { get; set; }
    }

    [Serializable]
    public sealed class OpeningTaskFrame
    {
        [JsonProperty("stage")] public int Stage { get; set; }
        [JsonProperty("met")] public bool Met { get; set; }
        [JsonProperty("worked")] public bool Worked { get; set; }
        [JsonProperty("drank")] public bool Drank { get; set; }
        [JsonProperty("done")] public bool Done { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("errand_done")] public bool ErrandDone { get; set; }
        [JsonProperty("issued")] public IssuedErrandFrame Issued { get; set; }
    }

    [Serializable]
    public sealed class JournalEntryFrame
    {
        [JsonProperty("seq")] public long Sequence { get; set; }
        [JsonProperty("day")] public long Day { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
    }

    [Serializable]
    public sealed class TalkFrame
    {
        [JsonProperty("with")] public string With { get; set; }
        [JsonProperty("who")] public string Who { get; set; }
        [JsonProperty("known")] public bool Known { get; set; }
        [JsonProperty("line")] public string Line { get; set; }
        [JsonProperty("options")] public List<TalkOptionFrame> Options { get; set; }
    }

    [Serializable]
    public sealed class TalkOptionFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
    }

    [Serializable]
    public sealed class InviteFromFrame
    {
        [JsonProperty("from")] public string From { get; set; }
        [JsonProperty("who")] public string Who { get; set; }
    }

    [Serializable]
    public sealed class SocialEventFrame
    {
        [JsonProperty("seq")] public long Sequence { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("with")] public string With { get; set; }
    }

    [Serializable]
    public sealed class WorldManifestFrame
    {
        [JsonProperty("scheme")] public string Scheme { get; set; }
        [JsonProperty("v")] public int Version { get; set; }
        [JsonProperty("units")] public string Units { get; set; }
        [JsonProperty("chunk_m")] public int ChunkMeters { get; set; }
        [JsonProperty("layer_m")] public int LayerMeters { get; set; }
        [JsonProperty("seamless")] public bool Seamless { get; set; }
        [JsonProperty("frames")] public List<WorldMapFrame> Frames { get; set; }
        [JsonProperty("rim_radius_m")] public double RimRadiusMeters { get; set; }
        [JsonProperty("settlements")] public List<WorldSettlementFrame> Settlements { get; set; }
        [JsonProperty("roads")] public List<WorldRoadFrame> Roads { get; set; }
    }

    [Serializable]
    public sealed class WorldMapFrame
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("projection")] public string Projection { get; set; }
        [JsonProperty("origin_lat")] public double OriginLatitude { get; set; }
        [JsonProperty("origin_lon")] public double OriginLongitude { get; set; }
        [JsonProperty("radius_m")] public double RadiusMeters { get; set; }
    }

    [Serializable]
    public sealed class WorldSettlementFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("e")] public double East { get; set; }
        [JsonProperty("n")] public double North { get; set; }
        [JsonProperty("u")] public double Up { get; set; }
        [JsonProperty("tier")] public string Tier { get; set; }
        [JsonProperty("terrain")] public string Terrain { get; set; }
        [JsonProperty("plan")] public string Plan { get; set; }
        [JsonProperty("cauldron")] public bool Cauldron { get; set; }
        [JsonProperty("half")] public double Half { get; set; }
        [JsonProperty("layout")] public string LayoutPath { get; set; }
    }

    [Serializable]
    public sealed class WorldRoadFrame
    {
        [JsonProperty("a")] public string From { get; set; }
        [JsonProperty("b")] public string To { get; set; }
    }

    [Serializable]
    public sealed class TerrainChunkFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("chunk")] public long[] Chunk { get; set; }
        [JsonProperty("origin")] public TerrainOriginFrame Origin { get; set; }
        [JsonProperty("size_m")] public int SizeMeters { get; set; }
        [JsonProperty("step_m")] public int StepMeters { get; set; }
        [JsonProperty("side")] public int Side { get; set; }
        [JsonProperty("heights")] public List<double> Heights { get; set; }
        [JsonProperty("settlements")] public List<TerrainSettlementFrame> Settlements { get; set; }
        public bool IsValid => Frame >= 0 && Chunk != null && Chunk.Length == 2 && Origin != null &&
            !double.IsNaN(Origin.East) && !double.IsInfinity(Origin.East) &&
            !double.IsNaN(Origin.North) && !double.IsInfinity(Origin.North) && SizeMeters == 256 && (StepMeters == 8 || StepMeters == 16 || StepMeters == 32 || StepMeters == 64) &&
            Side == SizeMeters / StepMeters + 1 && Heights != null && Heights.Count == Side * Side &&
            Heights.TrueForAll(v => !double.IsNaN(v) && !double.IsInfinity(v));
    }

    [Serializable]
    public sealed class TerrainOriginFrame
    {
        [JsonProperty("e")] public double East { get; set; }
        [JsonProperty("n")] public double North { get; set; }
    }

    [Serializable]
    public sealed class TerrainSettlementFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("e")] public double East { get; set; }
        [JsonProperty("n")] public double North { get; set; }
    }

    [Serializable]
    public sealed class WorldLayoutFrame
    {
        [JsonProperty("version")] public int Version { get; set; }
        [JsonProperty("world_half")] public double WorldHalf { get; set; }
        [JsonProperty("village_half")] public double VillageHalf { get; set; }
        [JsonProperty("buildings")] public List<WorldRectFrame> Buildings { get; set; }
        [JsonProperty("river")] public WorldRiverFrame River { get; set; }
        [JsonProperty("pois")] public Dictionary<string, WorldPointFrame> Pois { get; set; }

        // v1.19 (additive): every settlement has its own layout, fetched with ?settlement=<place.id>.
        /// <summary>"starter" for the hand-made tutorial village; otherwise a generated plan.</summary>
        [JsonProperty("plan")] public string Plan { get; set; }
        /// <summary>Null on pre-v1.19 servers, where the river always exists.</summary>
        [JsonProperty("has_river")] public bool? HasRiver { get; set; }
        [JsonProperty("zones")] public List<WorldZoneFrame> Zones { get; set; }
        /// <summary>Road graph edges as [x0, y0, x1, y1] in settlement-local metres.</summary>
        [JsonProperty("roads")] public List<double[]> Roads { get; set; }
        [JsonProperty("homes")] public List<WorldPointFrame> Homes { get; set; }
        [JsonProperty("entry")] public WorldPointFrame Entry { get; set; }
        /// <summary>Seamless world: land beyond world_half is open ground, not a wall.</summary>
        [JsonProperty("open")] public bool Open { get; set; }
        [JsonProperty("settlement")] public string Settlement { get; set; }

        /// <summary>True when the layout was generated for a non-tutorial settlement.</summary>
        public bool IsGenerated => !string.IsNullOrEmpty(Plan) && Plan != "starter";

        /// <summary>True when the river band is real geometry inside the settlement land.</summary>
        public bool RiverPresent => River != null && (HasRiver ?? true);

        public bool IsBlocked(double x, double y)
        {
            var inside = x >= -WorldHalf && x <= WorldHalf && y >= -WorldHalf && y <= WorldHalf;
            if (!inside && !Open) return true;
            if (Buildings != null)
                foreach (var b in Buildings)
                    if (b != null && x >= b.X0 && x <= b.X1 && y >= b.Y0 && y <= b.Y1) return true;
            return inside && RiverPresent && y >= River.Y0 && y <= River.Y1 &&
                (x < River.BridgeX0 || x > River.BridgeX1);
        }

        public Vector2 ResolveMove(Vector2 origin, Vector2 desired)
        {
            if (!IsBlocked(desired.x, desired.y)) return desired;
            if (!IsBlocked(desired.x, origin.y)) return new Vector2(desired.x, origin.y);
            if (!IsBlocked(origin.x, desired.y)) return new Vector2(origin.x, desired.y);
            return origin;
        }
    }

    [Serializable]
    public sealed class WorldRectFrame
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("x0")] public double X0 { get; set; }
        [JsonProperty("y0")] public double Y0 { get; set; }
        [JsonProperty("x1")] public double X1 { get; set; }
        [JsonProperty("y1")] public double Y1 { get; set; }
    }

    [Serializable]
    public sealed class WorldZoneFrame
    {
        /// <summary>forest | copse | field | meadow | rock | pond</summary>
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("x0")] public double X0 { get; set; }
        [JsonProperty("y0")] public double Y0 { get; set; }
        [JsonProperty("x1")] public double X1 { get; set; }
        [JsonProperty("y1")] public double Y1 { get; set; }
    }

    [Serializable]
    public sealed class WorldRiverFrame
    {
        [JsonProperty("y0")] public double Y0 { get; set; }
        [JsonProperty("y1")] public double Y1 { get; set; }
        [JsonProperty("bridge_x0")] public double BridgeX0 { get; set; }
        [JsonProperty("bridge_x1")] public double BridgeX1 { get; set; }
    }

    [Serializable]
    public sealed class WorldPointFrame
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
    }

    [Serializable]
    public sealed class AuthRequest
    {
        [JsonProperty("login")] public string Login { get; set; }
        [JsonProperty("pass")] public string Password { get; set; }
    }

    [Serializable]
    public sealed class AuthResponse
    {
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("token")] public string Token { get; set; }
        [JsonProperty("msg")] public string Message { get; set; }
    }
}
