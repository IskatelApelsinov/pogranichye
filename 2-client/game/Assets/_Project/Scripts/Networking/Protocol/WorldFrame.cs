using System;
using System.Collections.Generic;
using Newtonsoft.Json;

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
        [JsonProperty("invite_from")] public InviteFrame InviteFrom { get; set; }
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
        [JsonProperty("neighbors")] public List<NeighborFrame> Neighbors { get; set; }
    }

    [Serializable]
    public sealed class NeighborFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("act")] public string Act { get; set; }
        [JsonProperty("act_code")] public string ActivityCode { get; set; }
        [JsonProperty("known")] public bool Known { get; set; }
        [JsonProperty("desc")] public string Description { get; set; }
        [JsonProperty("pos")] public ScenePositionFrame Position { get; set; }
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
    public sealed class InviteFrame
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("desc")] public string Description { get; set; }
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
