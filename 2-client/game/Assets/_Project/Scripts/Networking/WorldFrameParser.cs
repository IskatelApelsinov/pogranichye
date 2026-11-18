using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Networking.Protocol;

namespace Pogranichye.Client.Networking
{
    public static class WorldFrameParser
    {
        public const int SupportedVersion = 1;

        public static WorldFrame Parse(string json)
        {
            return Parse(json, out _);
        }

        /// <summary>
        /// Parses and validates a full frame once, returning both its typed DTO and the
        /// exact validated token tree. The latter preserves absent-vs-default fields for
        /// legacy consumers during the staged migration away from JObject selectors.
        /// </summary>
        public static WorldFrame Parse(string json, out JObject document)
        {
            document = null;
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("Received an empty server frame.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new FormatException("Server frame is not a valid JSON object.", ex);
            }

            var versionToken = root["v"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
                throw new FormatException("Field v must be an integer.");

            var version = versionToken.Value<int>();
            if (version != SupportedVersion)
                throw new NotSupportedException(
                    $"Protocol version {version} is not supported. Client supports v{SupportedVersion}.");

            var hourToken = root["hour"];
            if (hourToken != null && hourToken.Type != JTokenType.Null &&
                (hourToken.Type != JTokenType.Integer || !int.TryParse(hourToken.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) || hour < 0 || hour > 23))
                throw new FormatException("Field hour must be an integer from 0 to 23.");

            var aliveToken = root["alive"];
            if (aliveToken == null || aliveToken.Type != JTokenType.Boolean)
                throw new FormatException("Field alive must be a boolean.");

            var alive = aliveToken.Value<bool>();
            if (alive)
            {
                var me = root["me"];
                if (me == null || me.Type != JTokenType.Object)
                    throw new FormatException("Alive frame is missing the me object.");
                RequireStringId(me["id"], "me.id");
                ActionResultFrameParser.Validate(me["result"], "me.result");
                CardGameFrameParser.Validate(me["card_game"], "me.card_game");
                CombatFrameParser.Validate(me["combat"], "me.combat");
                JourneyFrameParser.Validate(me["journey"], "me.journey");
                QuestsFrameParser.Validate(me["quests"], "me.quests");
                // Stats are presentation/gameplay values, not a reason to reject the whole world snapshot.
                // Keep the raw DTO conversion tolerant; missing or malformed stats must not block world entry.
                NormalizeStatsForClient(me);
                CarryBagFrameParser.Validate(me["bag"], "me.bag");
                HorseFrameParser.Validate(me["horse"], "me.horse");
                HorseFrameParser.ValidateArray(me["horses"], "me.horses");
                CharacterModeFrameParser.Validate(me["mode"], "me.mode");
                CharacterVigorFrameParser.Validate(me["vigor"], "me.vigor");
                RegionFrameParser.Validate(me["region"], "me.region");
                FeedFrameParser.Validate(me["feed"], "me.feed");
                ValidateJournal(me["journal"], "me.journal");
                ValidateWorldPosition(me["wpos"], "me.wpos");

                var place = root["place"];
                var hasWorldAnchor = place != null && place.Type == JTokenType.Object &&
                    place["anchor"] != null && place["anchor"].Type == JTokenType.Object;
                // In seamless mode the server's handover hysteresis may temporarily leave
                // me.pos outside the 150 m settlement footprint. When global coordinates and
                // the matching settlement anchor are present, validate the local/global relation
                // below instead of rejecting a valid world position at the old scene boundary.
                ValidatePosition(me["pos"], "me.pos", true, me["wpos"] != null && hasWorldAnchor);
                ValidateRoom(me["room"], "me.room");
                if (place != null && place.Type != JTokenType.Null)
                {
                    if (place.Type != JTokenType.Object)
                        throw new FormatException("Field place must be an object.");
                    ValidateWorldAnchor(place["anchor"], "place.anchor");
                    // Optional POI metadata must not reject the authoritative world snapshot.
                    NormalizeTavernCounterForClient(place);
                    NormalizeWildSitesForClient(place);
                    // v1.30 optional nearby pickups: discard malformed entries individually so
                    // a bad presentation hint can never reject the authoritative world snapshot.
                    place["gather"] = GatherNodeFrameParser.Normalize(place["gather"]);
                    TavernCounterFrameParser.Validate(place["tavern_counter"], "place.tavern_counter");
                    FoeFrameParser.Validate(place["foes"], "place.foes");
                    ValidatePlaceRooms(place["rooms"], "place.rooms");
                    var neighbors = place["neighbors"];
                    if (neighbors != null && neighbors.Type != JTokenType.Null)
                    {
                        if (neighbors.Type != JTokenType.Array)
                            throw new FormatException("place.neighbors must be an array.");
                        var neighborArray = (JArray)neighbors;
                        for (var i = 0; i < neighborArray.Count; i++)
                        {
                            var neighbor = neighborArray[i];
                            if (neighbor == null || neighbor.Type != JTokenType.Object)
                                throw new FormatException($"place.neighbors[{i}] должен быть объектом.");
                            RequireStringId(neighbor["id"], $"place.neighbors[{i}].id");
                            ValidatePosition(neighbor["pos"], $"place.neighbors[{i}].pos", false);
                            ValidateWorldPosition(neighbor["wpos"], $"place.neighbors[{i}].wpos");
                        }
                    }
                }
            }

            WorldFrame frame;
            try
            {
                frame = root.ToObject<WorldFrame>();
            }
            catch (JsonException ex)
            {
                throw new FormatException("Failed to convert the v1 frame to a DTO.", ex);
            }

            if (frame == null)
                throw new FormatException("Failed to parse the server frame.");
            if (frame.Me != null && frame.Me.WorldPosition != null && !frame.Me.WorldPosition.IsValid)
                throw new FormatException("me.wpos: coordinates, chunk, and layer are inconsistent.");
            if (frame.Me?.WorldPosition != null && frame.Place?.Anchor != null && frame.Me.Position != null)
            {
                if (Math.Abs(frame.Me.WorldPosition.East - frame.Place.Anchor.East - frame.Me.Position.X) > 0.002d ||
                    Math.Abs(frame.Me.WorldPosition.North - frame.Place.Anchor.North - frame.Me.Position.Y) > 0.002d)
                    throw new FormatException("me.wpos does not match place.anchor + me.pos.");
            }
            if (frame.Me != null && frame.Me.Room != null)
            {
                ParseEntityId(frame.Me.Room.Id, "me.room.id");
                ParseEntityId(frame.Me.Room.Settlement, "me.room.settlement");
            }
            if (frame.Place?.Neighbors != null)
            {
                for (var i = 0; i < frame.Place.Neighbors.Count; i++)
                {
                    var neighbor = frame.Place.Neighbors[i];
                    if (neighbor?.WorldPosition != null && !neighbor.WorldPosition.IsValid)
                        throw new FormatException($"place.neighbors[{i}].wpos: coordinates, chunk, and layer are inconsistent.");
                    if (neighbor?.WorldPosition != null && frame.Place.Anchor != null &&
                        neighbor.WorldPosition.Frame != frame.Place.Anchor.Frame)
                        throw new FormatException($"place.neighbors[{i}].wpos.frame does not match place.anchor.frame.");
                    if (neighbor?.WorldPosition != null && neighbor.Position != null && frame.Place.Anchor != null &&
                        (Math.Abs(neighbor.WorldPosition.East - frame.Place.Anchor.East - neighbor.Position.X) > 0.002d ||
                         Math.Abs(neighbor.WorldPosition.North - frame.Place.Anchor.North - neighbor.Position.Y) > 0.002d))
                        throw new FormatException($"place.neighbors[{i}].wpos does not match anchor + local pos.");
                }
            }
            if (frame.Place?.Rooms != null &&
                (frame.Place.Rooms.Total < 0 || frame.Place.Rooms.Vacant < 0 ||
                 frame.Place.Rooms.Vacant > frame.Place.Rooms.Total ||
                 !IsFinite(frame.Place.Rooms.RentPrice) || frame.Place.Rooms.RentPrice < 0d))
                throw new FormatException("place.rooms contains an invalid room summary.");
            if (frame.Me != null && frame.Me.Room != null &&
                (frame.Me.Room.LeaseState != "vacant" && frame.Me.Room.LeaseState != "active" &&
                 frame.Me.Room.LeaseState != "grace" && frame.Me.Room.LeaseState != "expired"))
                throw new FormatException("me.room.lease_state contains an unknown lease state.");
            if (frame.Place != null && frame.Place.Anchor != null && frame.Me != null &&
                frame.Me.WorldPosition != null && frame.Place.Anchor.Frame != frame.Me.WorldPosition.Frame)
                throw new FormatException("place.anchor.frame does not match me.wpos.frame.");
            if (frame.Alive)
            {
                ParseEntityId(frame.Me.Id, "me.id");
                if (frame.Place?.Neighbors != null)
                {
                    for (var i = 0; i < frame.Place.Neighbors.Count; i++)
                    {
                        var neighbor = frame.Place.Neighbors[i];
                        if (neighbor == null)
                            throw new FormatException($"place.neighbors[{i}] is null.");
                        ParseEntityId(neighbor.Id, $"place.neighbors[{i}].id");
                    }
                }
            }
            document = root;
            return frame;
        }


        private static void NormalizeWildSitesForClient(JToken placeToken)
        {
            var place = placeToken as JObject;
            if (place == null) return;
            var token = place["sites"];
            // Additive v1.22 presentation data: malformed optional sites are hidden individually,
            // never allowed to reject the authoritative character/world snapshot.
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Array)
            {
                place["sites"] = new JArray();
                return;
            }

            var valid = new JArray();
            foreach (var entry in (JArray)token)
            {
                if (entry == null || entry.Type != JTokenType.Object) continue;
                var id = entry["id"];
                var name = entry["name"];
                var kind = entry["kind"];
                var direction = entry["dir"];
                var here = entry["here"];
                if (id?.Type != JTokenType.String || string.IsNullOrWhiteSpace(id.Value<string>()) ||
                    name?.Type != JTokenType.String || string.IsNullOrWhiteSpace(name.Value<string>()) ||
                    kind?.Type != JTokenType.String || direction?.Type != JTokenType.String ||
                    here?.Type != JTokenType.Boolean ||
                    !IsNumber(entry["e"]) || !IsFinite(entry["e"].Value<double>()) ||
                    !IsNumber(entry["n"]) || !IsFinite(entry["n"].Value<double>()) ||
                    !IsNumber(entry["u"]) || !IsFinite(entry["u"].Value<double>()) ||
                    !IsNumber(entry["dist"]) || !IsFinite(entry["dist"].Value<double>()) ||
                    entry["dist"].Value<double>() < 0d ||
                    entry["acts"]?.Type != JTokenType.Array) continue;
                var actionsValid = true;
                foreach (var action in (JArray)entry["acts"])
                    if (action?.Type != JTokenType.String ||
                        (action.Value<string>() != "rest" && action.Value<string>() != "drink" &&
                         action.Value<string>() != "explore" && action.Value<string>() != "forage"))
                        actionsValid = false;
                if (!actionsValid) continue;
                if (entry["gives"] != null && entry["gives"].Type != JTokenType.String) continue;
                if (entry["left"] != null &&
                    (!IsNumber(entry["left"]) || !IsFinite(entry["left"].Value<double>()) || entry["left"].Value<double>() < 0d))
                    continue;
                valid.Add(entry);
            }
            place["sites"] = valid;
        }

        private static void NormalizeTavernCounterForClient(JToken placeToken)
        {
            var place = placeToken as JObject;
            if (place == null) return;

            var counter = place["tavern_counter"];
            if (counter == null || counter.Type == JTokenType.Null) return;

            try
            {
                TavernCounterFrameParser.Validate(counter, "place.tavern_counter");
            }
            catch (FormatException)
            {
                // Invalid optional geometry means the interaction is unavailable.
                // Do not invent coordinates or radius; hide this POI for this frame.
                place["tavern_counter"] = JValue.CreateNull();
            }
        }

        private static void NormalizeStatsForClient(JToken meToken)
        {
            var me = meToken as JObject;
            if (me == null) return;

            var source = me["stats"] as JObject;
            var normalized = new JObject();
            foreach (var key in new[] { "strength", "agility", "endurance", "intellect", "perception", "will" })
            {
                var token = source != null ? source[key] : null;
                double value;
                if (token == null ||
                    (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) ||
                    !TryReadFiniteNumber(token, out value) ||
                    double.IsNaN(value) || double.IsInfinity(value))
                    value = 0d;

                normalized[key] = value;
            }

            // Keep unknown future fields so additive protocol extensions remain available.
            if (source != null)
                foreach (var property in source.Properties())
                    if (normalized[property.Name] == null)
                        normalized[property.Name] = property.Value.DeepClone();

            me["stats"] = normalized;
        }

        private static void ValidateWorldPosition(JToken token, string fieldName)
        {
            // Optional additive v1.19 field: older v:1 servers remain compatible.
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Object ||
                token["scheme"]?.Type != JTokenType.String || token["scheme"].Value<string>() != "yof-tangent" ||
                token["v"]?.Type != JTokenType.Integer ||
                !int.TryParse(token["v"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version != 1 ||
                token["frame"]?.Type != JTokenType.Integer ||
                !int.TryParse(token["frame"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame) || frame < 0)
                throw new FormatException(fieldName + " uses an unsupported coordinate schema.");
            foreach (var key in new[] { "e", "n", "u", "ground", "z" })
            {
                if (!IsNumber(token[key]) || !IsFinite(token[key].Value<double>()))
                    throw new FormatException(fieldName + "." + key + " must be a finite number in meters.");
            }
            var chunk = token["chunk"];
            if (chunk == null || chunk.Type != JTokenType.Array || ((JArray)chunk).Count != 2)
                throw new FormatException(fieldName + ".chunk must contain two integer indices.");
            for (var i = 0; i < 2; i++)
                if (chunk[i].Type != JTokenType.Integer ||
                    !long.TryParse(chunk[i].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new FormatException(fieldName + ".chunk contains an index outside the int64 range.");
            if (token["layer"] == null || token["layer"].Type != JTokenType.Integer ||
                !long.TryParse(token["layer"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                throw new FormatException(fieldName + ".layer must be an int64 integer.");
        }

        private static void ValidateRoom(JToken token, string fieldName)
        {
            // Optional additive v1.20 field: null for players without a room and on old servers.
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Object)
                throw new FormatException(fieldName + " должен быть объектом или null.");
            RequireStringId(token["id"], fieldName + ".id");
            RequireStringId(token["settlement"], fieldName + ".settlement");
            if (token["door_poi"]?.Type != JTokenType.String ||
                token["lease_state"]?.Type != JTokenType.String ||
                token["indoor"]?.Type != JTokenType.Boolean)
                throw new FormatException(fieldName + " must contain door_poi, lease_state, and indoor.");
            var state = token["lease_state"].Value<string>();
            if (state != "vacant" && state != "active" && state != "grace" && state != "expired")
                throw new FormatException(fieldName + ".lease_state contains an unknown state.");
            foreach (var key in new[] { "rent_price", "chest_cap", "chest_load" })
                if (token[key] != null && (!IsNumber(token[key]) || !IsFinite(token[key].Value<double>()) || token[key].Value<double>() < 0d))
                    throw new FormatException(fieldName + "." + key + " must be a finite non-negative number.");
            foreach (var key in new[] { "paid_through_day", "grace_until_day" })
                if (token[key] != null && (token[key].Type != JTokenType.Integer ||
                    !long.TryParse(token[key].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) || day < 0))
                    throw new FormatException(fieldName + "." + key + " must be a non-negative integer.");
            if (token["chest_items"] != null && token["chest_items"].Type != JTokenType.Object)
                throw new FormatException(fieldName + ".chest_items must be an object.");
        }

        private static void ValidatePlaceRooms(JToken token, string fieldName)
        {
            // Optional additive v1.20 summary. Never includes private chest contents.
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Object ||
                token["total"]?.Type != JTokenType.Integer ||
                token["vacant"]?.Type != JTokenType.Integer ||
                !IsNumber(token["rent_price"]))
                throw new FormatException(fieldName + " must contain total, vacant, and rent_price.");
            var items = token["items"];
            if (items == null || items.Type == JTokenType.Null) return;
            if (items.Type != JTokenType.Array)
                throw new FormatException(fieldName + ".items must be an array.");
            var entries = (JArray)items;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.Type != JTokenType.Object)
                    throw new FormatException(fieldName + ".items[" + i + "] must be an object.");
                RequireStringId(entry["id"], fieldName + ".items[" + i + "].id");
                var state = entry["lease_state"]?.Value<string>();
                if (entry["available"]?.Type != JTokenType.Boolean)
                    throw new FormatException(fieldName + ".items[" + i + "].available must be a boolean.");
                if (state != "vacant" && state != "active" && state != "grace" && state != "expired")
                    throw new FormatException(fieldName + ".items[" + i + "].lease_state contains an unknown state.");
                if (!IsNumber(entry["rent_price"]) || !IsFinite(entry["rent_price"].Value<double>()) ||
                    entry["rent_price"].Value<double>() < 0d)
                    throw new FormatException(fieldName + ".items[" + i + "].rent_price must be a non-negative number.");
            }
        }

        private static void ValidateWorldAnchor(JToken token, string fieldName)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Object || token["frame"]?.Type != JTokenType.Integer ||
                !int.TryParse(token["frame"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame) || frame < 0)
                throw new FormatException(fieldName + " must contain a non-negative integer frame.");
            foreach (var key in new[] { "e", "n", "u" })
                if (!IsNumber(token[key]) || !IsFinite(token[key].Value<double>()))
                    throw new FormatException(fieldName + "." + key + " must be a finite number in meters.");
        }

        private static void ValidateJournal(JToken token, string fieldName)
        {
            // Optional additive v1 field: old servers may omit it or send null.
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.Array)
                throw new FormatException(fieldName + " должен быть массивом.");

            var entries = (JArray)token;
            long previousSequence = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.Type != JTokenType.Object)
                    throw new FormatException(fieldName + "[" + i + "] must be an object.");

                var sequence = entry["seq"];
                if (sequence == null || sequence.Type != JTokenType.Integer ||
                    !long.TryParse(sequence.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var sequenceValue) ||
                    sequenceValue <= previousSequence)
                    throw new FormatException(fieldName + "[" + i + "].seq must be a strictly increasing positive int64.");

                var day = entry["day"];
                if (day == null || day.Type != JTokenType.Integer ||
                    !long.TryParse(day.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var dayValue) || dayValue < 0)
                    throw new FormatException(fieldName + "[" + i + "].day must be a non-negative int64 integer.");

                var kind = entry["kind"];
                if (kind == null || kind.Type != JTokenType.String || string.IsNullOrWhiteSpace(kind.Value<string>()))
                    throw new FormatException(fieldName + "[" + i + "].kind must be a non-empty string.");

                var text = entry["text"];
                if (text == null || text.Type != JTokenType.String || string.IsNullOrWhiteSpace(text.Value<string>()))
                    throw new FormatException(fieldName + "[" + i + "].text must be a non-empty string.");

                previousSequence = sequenceValue;
            }
        }

        private static void ValidatePosition(JToken token, string fieldName, bool player, bool allowExtendedWorldPosition = false)
        {
            if (token == null || token.Type == JTokenType.Null) return; // v1.0 compatibility
            if (token.Type != JTokenType.Object)
                throw new FormatException(fieldName + " должен быть объектом.");

            if (!IsNumber(token["x"]) || !IsNumber(token["y"]) || !IsNumber(token["facing"]))
                throw new FormatException(fieldName + " must contain numeric x, y, and facing values.");
            var xValue = token["x"].Value<double>();
            var yValue = token["y"].Value<double>();
            var facingValue = token["facing"].Value<double>();
            var maxCoordinate = allowExtendedWorldPosition ? 1000000000d : 150.001d;
            if (!IsFinite(xValue) || !IsFinite(yValue) ||
                Math.Abs(xValue) > maxCoordinate || Math.Abs(yValue) > maxCoordinate)
                throw new FormatException(allowExtendedWorldPosition
                    ? fieldName + " coordinates must be finite and within the global world range."
                    : fieldName + " coordinates must be finite and within the ±150 m world bounds.");
            if (!IsFinite(facingValue) || facingValue < 0d || facingValue >= 360d)
                throw new FormatException(fieldName + ".facing must be a finite number in the [0, 360) range.");

            if (player)
            {
                var stamina = token["stam"];
                var sequence = token["seq"];
                if (!IsNumber(stamina))
                    throw new FormatException(fieldName + ".stam must be a number.");
                if (sequence == null || sequence.Type != JTokenType.Integer ||
                    !long.TryParse(sequence.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequenceValue) ||
                    sequenceValue < 0)
                    throw new FormatException(fieldName + ".seq must be a non-negative int64 integer.");
                var staminaValue = stamina.Value<double>();
                if (!IsFinite(staminaValue) || staminaValue < 0d || staminaValue > 1d)
                    throw new FormatException(fieldName + ".stam must be in the 0..1 range.");
            }
        }

        private static bool TryReadFiniteNumber(JToken token, out double value)
        {
            value = 0d;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) return false;
            try
            {
                value = token.Value<double>();
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch (Exception) { return false; }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsNumber(JToken token)
        {
            return token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float);
        }

        private static void RequireStringId(JToken token, string fieldName)
        {
            if (token == null || token.Type != JTokenType.String)
                throw new FormatException($"{fieldName} must be a JSON string containing a uint64 ID.");
            ParseEntityId(token.Value<string>(), fieldName);
        }

        public static ulong ParseEntityId(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                throw new FormatException($"{fieldName} must be a string-form uint64 ID.");
            }
            return id;
        }
    }
}
