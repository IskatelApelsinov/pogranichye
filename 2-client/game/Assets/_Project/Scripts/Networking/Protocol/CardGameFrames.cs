using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class CardGameFrame
    {
        [JsonProperty("game")] public string Game { get; set; }
        [JsonProperty("phase")] public string Phase { get; set; }
        [JsonProperty("stake")] public double Stake { get; set; }
        [JsonProperty("done")] public bool Done { get; set; }
        [JsonProperty("result")] public string Result { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("player")] public List<CardGameCardFrame> Player { get; set; }
        // Ochko exposes one dealer card until settlement; Durak exposes only a count.
        [JsonProperty("dealer")] public List<CardGameCardFrame> Dealer { get; set; }
        [JsonProperty("dealer_cards")] public int DealerCardCount { get; set; }
        [JsonProperty("ptotal")] public int PlayerTotal { get; set; }
        [JsonProperty("dtotal")] public int? DealerTotal { get; set; }
        [JsonProperty("hidden")] public int HiddenDealerCards { get; set; }
        [JsonProperty("trump")] public string Trump { get; set; }
        [JsonProperty("trump_card")] public string TrumpCard { get; set; }
        [JsonProperty("deck")] public int DeckCount { get; set; }
        [JsonProperty("attacker")] public string Attacker { get; set; }
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("your_turn")] public bool YourTurn { get; set; }
        [JsonProperty("table")] public List<DurakTablePairFrame> Table { get; set; }
    }

    [Serializable]
    public sealed class CardGameCardFrame
    {
        [JsonProperty("c")] public string Code { get; set; }
        [JsonProperty("v")] public int Value { get; set; }
        [JsonProperty("rank")] public int Rank { get; set; }
        [JsonProperty("suit")] public string Suit { get; set; }
    }

    [Serializable]
    public sealed class DurakTablePairFrame
    {
        [JsonProperty("a")] public CardGameCardFrame Attack { get; set; }
        [JsonProperty("d")] public CardGameCardFrame Defence { get; set; }
    }

    /// <summary>Validates the exact public card-game view. Private dealer hands must never cross the wire.</summary>
    public static class CardGameFrameParser
    {
        public static void Validate(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var obj = token as JObject;
            if (obj == null) throw new FormatException(path + " должен быть объектом или null.");

            var game = RequireString(obj["game"], path + ".game");
            var phase = RequireString(obj["phase"], path + ".phase");
            RequireFiniteNonNegative(obj["stake"], path + ".stake");
            RequireBoolean(obj["done"], path + ".done");
            RequireOptionalString(obj["result"], path + ".result");
            RequireOptionalString(obj["message"], path + ".message");
            ValidateCards(obj["player"], path + ".player", game);

            if (game == "ochko") ValidateOchko(obj, path);
            else if (game == "durak") ValidateDurak(obj, path, phase);
            else throw new FormatException(path + ".game содержит неизвестную игру.");
        }

        private static void ValidateOchko(JObject obj, string path)
        {
            var phase = RequireString(obj["phase"], path + ".phase");
            var done = RequireBoolean(obj["done"], path + ".done");
            if (phase != (done ? "done" : "playing"))
                throw new FormatException(path + ".phase не соответствует done.");

            ValidateCards(obj["dealer"], path + ".dealer", "ochko");
            var dealer = (JArray)obj["dealer"];
            if (!done && dealer.Count > 1)
                throw new FormatException(path + ".dealer раскрывает скрытые карты корчмаря.");
            var hidden = RequireNonNegativeInteger(obj["hidden"], path + ".hidden");
            if (done && hidden != 0)
                throw new FormatException(path + ".hidden должен быть равен нулю после завершения.");
            var total = obj["dtotal"];
            if (total != null && total.Type != JTokenType.Null)
                RequireNonNegativeInteger(total, path + ".dtotal");
            if (done && (total == null || total.Type == JTokenType.Null))
                throw new FormatException(path + ".dtotal должен быть известен после завершения.");
        }

        private static void ValidateDurak(JObject obj, string path, string phase)
        {
            if (phase != "attack" && phase != "defend" && phase != "over")
                throw new FormatException(path + ".phase содержит неизвестную фазу Дурака.");

            // DurakView intentionally sends only dealer_cards. A populated dealer array is a privacy leak.
            var dealer = obj["dealer"];
            if (dealer != null && dealer.Type != JTokenType.Null &&
                (dealer.Type != JTokenType.Array || ((JArray)dealer).Count > 0))
                throw new FormatException(path + ".dealer не должен раскрывать руку корчмаря в Дураке.");

            RequireNonNegativeInteger(obj["dealer_cards"], path + ".dealer_cards");
            RequireNonNegativeInteger(obj["deck"], path + ".deck");
            RequireString(obj["trump"], path + ".trump");
            RequireOptionalString(obj["trump_card"], path + ".trump_card");
            var attacker = RequireString(obj["attacker"], path + ".attacker");
            if (attacker != "player" && attacker != "dealer")
                throw new FormatException(path + ".attacker содержит неизвестное место.");
            var role = RequireString(obj["role"], path + ".role");
            var expectedRole = attacker == "player" ? "attack" : "defend";
            if (role != expectedRole)
                throw new FormatException(path + ".role не соответствует атакующему.");
            var done = RequireBoolean(obj["done"], path + ".done");
            var yourTurn = RequireBoolean(obj["your_turn"], path + ".your_turn");
            if (done != (phase == "over"))
                throw new FormatException(path + ".phase не соответствует done.");
            var expectedTurn = !done &&
                ((attacker == "player" && phase == "attack") ||
                 (attacker == "dealer" && phase == "defend"));
            if (yourTurn != expectedTurn)
                throw new FormatException(path + ".your_turn не соответствует фазе и роли.");
            var playerTotal = obj["ptotal"];
            if (playerTotal != null && playerTotal.Type != JTokenType.Null)
                RequireNonNegativeInteger(playerTotal, path + ".ptotal");

            var tableToken = obj["table"];
            if (tableToken == null || tableToken.Type != JTokenType.Array)
                throw new FormatException(path + ".table должен быть массивом.");
            var table = (JArray)tableToken;
            for (var i = 0; i < table.Count; i++)
            {
                var pair = table[i] as JObject;
                if (pair == null) throw new FormatException(path + ".table[" + i + "] должен быть объектом.");
                ValidateCard(pair["a"], path + ".table[" + i + "].a", "durak");
                var defence = pair["d"];
                if (defence != null && defence.Type != JTokenType.Null)
                    ValidateCard(defence, path + ".table[" + i + "].d", "durak");
            }
        }

        private static void ValidateCards(JToken token, string path, string game)
        {
            if (token == null || token.Type != JTokenType.Array)
                throw new FormatException(path + " должен быть массивом.");
            var cards = (JArray)token;
            for (var i = 0; i < cards.Count; i++)
                ValidateCard(cards[i], path + "[" + i + "]", game);
        }

        private static void ValidateCard(JToken token, string path, string game)
        {
            var card = token as JObject;
            if (card == null) throw new FormatException(path + " должен быть объектом.");
            RequireString(card["c"], path + ".c");
            if (game == "ochko")
                RequireNonNegativeInteger(card["v"], path + ".v");
            else
            {
                var rank = RequireNonNegativeInteger(card["rank"], path + ".rank");
                if (rank < 6 || rank > 14)
                    throw new FormatException(path + ".rank должен быть от 6 до 14.");
                RequireString(card["suit"], path + ".suit");
            }
        }

        private static string RequireString(JToken token, string path)
        {
            if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
                throw new FormatException(path + " должен быть непустой строкой.");
            return token.Value<string>();
        }

        private static void RequireOptionalString(JToken token, string path)
        {
            if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
                throw new FormatException(path + " должен быть строкой или null.");
        }

        private static bool RequireBoolean(JToken token, string path)
        {
            if (token == null || token.Type != JTokenType.Boolean)
                throw new FormatException(path + " должен быть boolean.");
            return token.Value<bool>();
        }

        private static int RequireNonNegativeInteger(JToken token, string path)
        {
            if (token == null || token.Type != JTokenType.Integer)
                throw new FormatException(path + " должен быть целым неотрицательным числом.");
            var value = token.Value<long>();
            if (value < 0 || value > int.MaxValue)
                throw new FormatException(path + " вне допустимого диапазона.");
            return (int)value;
        }

        private static void RequireFiniteNonNegative(JToken token, string path)
        {
            double value;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) ||
                !JsonNumberReader.TryReadFiniteDouble(token, out value) || value < 0d || value > 1000000000d)
                throw new FormatException(path + " должен быть конечным неотрицательным числом.");
        }
    }
}
