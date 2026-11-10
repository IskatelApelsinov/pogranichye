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
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("tier")] public string Tier { get; set; }
        [JsonProperty("food")] public double Food { get; set; }
        [JsonProperty("bread")] public double Bread { get; set; }
        [JsonProperty("bread_price")] public double BreadPrice { get; set; }
        [JsonProperty("tavern")] public bool Tavern { get; set; }
        [JsonProperty("beer")] public double Beer { get; set; }
        [JsonProperty("beer_price")] public double BeerPrice { get; set; }
        [JsonProperty("neighbors")] public List<NeighborFrame> Neighbors { get; set; }
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
    public sealed class OpeningTaskFrame
    {
        [JsonProperty("stage")] public int Stage { get; set; }
        [JsonProperty("met")] public bool Met { get; set; }
        [JsonProperty("worked")] public bool Worked { get; set; }
        [JsonProperty("drank")] public bool Drank { get; set; }
        [JsonProperty("done")] public bool Done { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
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
    public sealed class WorldLayoutFrame
    {
        [JsonProperty("version")] public int Version { get; set; }
        [JsonProperty("world_half")] public double WorldHalf { get; set; }
        [JsonProperty("village_half")] public double VillageHalf { get; set; }
        [JsonProperty("buildings")] public List<WorldRectFrame> Buildings { get; set; }
        [JsonProperty("river")] public WorldRiverFrame River { get; set; }
        [JsonProperty("pois")] public Dictionary<string, WorldPointFrame> Pois { get; set; }

        public bool IsBlocked(double x, double y)
        {
            if (x < -WorldHalf || x > WorldHalf || y < -WorldHalf || y > WorldHalf) return true;
            if (Buildings != null)
                foreach (var b in Buildings)
                    if (b != null && x >= b.X0 && x <= b.X1 && y >= b.Y0 && y <= b.Y1) return true;
            return River != null && y >= River.Y0 && y <= River.Y1 &&
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
