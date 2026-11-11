using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Presentation;
using UnityEngine;

namespace Pogranichye.Client
{
    /// <summary>
    /// Core gameplay of protocol v1.10 for the thick client: first-person combat (melee, bow, tricks,
    /// stances), weave magic traced with the mouse, need-driven requests, crafting, road journeys and
    /// the party. Additive module: it reads the extra frame fields straight from JSON and only sends
    /// intents; every outcome is decided by the server and shown from confirmed frames (me.feed,
    /// me.result, place.foes). Nothing here predicts hits, damage, loot or quest progress.
    ///
    /// Input is separated from resolution (project canon): in first person the client aims itself and
    /// reports "this foe, this body part, this accuracy"; the server checks range and view cone and
    /// resolves the strike. In third person the click only selects a target and the server rolls dice.
    /// </summary>
    public sealed class CoreGameplay : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private sealed class FoeRec
        {
            public GameObject Root;
            public CoreFoeView View;
            public Vector3 Target;
            public float Yaw;
            public float Hp;
            public float Max;
            public bool Aware;
            public string State;
            public float Punch; // seconds of the "got hit" squash
            public ProceduralGait Gait;
        }

        private const float PendingTimeout = 6f;

        private sealed class Floater
        {
            public Vector3 World;
            public string Text;
            public float At;
            public bool Hurt;
        }

        private sealed class FeedLine
        {
            public string Kind;
            public string Text;
            public float At;
        }

        // Mirror of Layout v2 POI anchors (metres, server x/y). Only used to decide which actions the
        // "Дела" window offers; the server validates the real position for every intent.
        private static readonly Dictionary<string, Vector2> Poi = new Dictionary<string, Vector2>
        {
            { "tavern", new Vector2(-33f, 12f) }, { "smithy", new Vector2(29f, 14f) },
            { "granary", new Vector2(-2f, 28f) }, { "market", new Vector2(1f, 4f) },
            { "well", new Vector2(0f, -4f) }, { "hearth", new Vector2(33f, -32f) },
            { "garden", new Vector2(70f, -20f) }, { "river", new Vector2(0f, -74f) },
            { "woods", new Vector2(-95f, 10f) }, { "mine", new Vector2(90f, 40f) }
        };

        private static readonly Dictionary<string, string> TakeAt = new Dictionary<string, string>
        {
            { "axe", "woods" }, { "rod", "river" }, { "sword", "smithy" }, { "bow", "smithy" },
            { "torch", "market" }, { "lantern", "market" }, { "cloak", "granary" }, { "boots", "granary" },
            { "hat", "granary" }
        };

        private static readonly Dictionary<string, string> Ru = new Dictionary<string, string>
        {
            { "axe", "топор" }, { "sword", "меч" }, { "bow", "лук" }, { "rod", "удочка" }, { "torch", "факел" },
            { "lantern", "фонарь" }, { "knife", "нож" }, { "cloak", "плащ" }, { "boots", "сапоги" },
            { "hat", "шапка" }, { "jerkin", "кожаная куртка" }, { "shield", "щит" }, { "arrow", "стрелы" },
            { "bandage", "повязка" }, { "potion", "снадобье" }, { "stew", "уха" }, { "roast", "жаркое" },
            { "bread", "хлеб" }, { "fish", "рыба" }, { "wood", "дерево" }, { "meat", "мясо" }, { "hide", "шкура" },
            { "wheat", "зерно" }, { "veg", "овощи" }, { "ore", "руда" }, { "metal", "железо" },
            { "leather", "кожа" }, { "herb", "травы" }, { "flour", "мука" }, { "game", "дичь" },
            { "cheese", "сыр" }, { "egg", "яйца" }, { "milk", "молоко" }, { "wool", "шерсть" },
            { "cloth", "сукно" }, { "tools", "инструмент" }, { "hay", "сено" }, { "feather", "перо" },
            { "flax", "лён" }, { "linen", "холст" }, { "remedy", "лекарство" }, { "honey", "мёд" },
            { "mead", "медовуха" }
        };

        private static readonly string[] Nodes = { "knot", "thread", "loop", "fork", "barb", "seal" };
        private static readonly string[] NodeRu = { "узел", "нить", "петля", "развилка", "шип", "печать" };
        // Short Cyrillic marks instead of symbol glyphs: the default IMGUI font is not guaranteed to have them.
        private static readonly string[] NodeGlyph = { "Уз", "Нт", "Пт", "Рз", "Шп", "Пч" };

        private Action<Dictionary<string, object>> _send;
        private bool _active;
        private bool _firstPerson;
        private bool _hudCollapsed;
        private Transform _player;
        private PresentationCameraRig _rig;
        private ClientInput _input;
        private JObject _me;
        private JObject _place;
        private int _hour = 12;

        private readonly Dictionary<string, FoeRec> _foes = new Dictionary<string, FoeRec>();
        private readonly Dictionary<string, GameObject> _exitViews = new Dictionary<string, GameObject>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly List<string> _stale = new List<string>();
        private Transform _foeRoot;
        private MaterialPropertyBlock _block;

        private readonly List<FeedLine> _feed = new List<FeedLine>();
        private readonly List<Floater> _floaters = new List<Floater>();
        private RequestGate _gate;
        private int _chopSwings;
        private int _fishPhase; // 0 idle, 1 waiting for a bite, 2 bite window
        private float _fishAt;
        private string _toast;
        private float _toastUntil;
        private bool _showHints = true;
        private long _feedSeq;
        private float _hurtFlash;
        private float _hitMarker;

        private bool _menuOpen;
        private Vector2 _menuScroll;
        private bool _weaveOpen;
        private readonly List<string> _pattern = new List<string>();
        private int _weaveLastNode = -1;
        private bool _holding;
        private float _holdStart;
        private bool _guardSent;
        private Light _handLight;

        /// <summary>True while the "Дела" window or the weave wheel owns the mouse.</summary>
        public bool CapturesInput { get { return _menuOpen || _weaveOpen; } }

        /// <summary>Raised on the Unity main thread when a queued result-bearing request resolves.</summary>
        public event Action<string, RequestGate.Outcome, string> RequestResolved;

        public void Bind(Action<Dictionary<string, object>> sender)
        {
            _send = sender;
        }

        public void BindInput(ClientInput input)
        {
            _input = input;
        }

        public void DrawInventoryTab()
        {
            if (!_active || _me == null)
            {
                GUILayout.Label("Инвентарь появится после подключения к миру.");
                return;
            }

            var inv = Obj(_me, "inv");
            var items = Obj(inv, "items");
            var worn = Obj(inv, "worn");
            var hand = Str(inv, "hand");
            var stash = Obj(_me, "stash");
            var bag = Obj(_me, "bag");
            var horse = Obj(_me, "horse");

            GUILayout.Label("ЭКИПИРОВКА");
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("В руке: " + (string.IsNullOrEmpty(hand) ? "пусто" : Name(hand)));
            if (!string.IsNullOrEmpty(hand) && GUILayout.Button("Убрать из руки"))
                Ask("Экипировка", "equip", "item", string.Empty);

            foreach (var item in new[] { "axe", "sword", "knife", "bow", "rod", "torch", "lantern" })
            {
                var quantity = Count(items, item);
                if (quantity < 1f) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Name(item) + " ×" + (int)quantity, GUILayout.Width(190f));
                GUI.enabled = hand != item;
                if (GUILayout.Button(hand == item ? "В руке" : "Взять в руку"))
                    Ask("Экипировка", "equip", "item", item);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4f);
            GUILayout.Label("Надето:");
            var hasWorn = false;
            if (worn != null)
            {
                foreach (var slot in worn.Properties())
                {
                    if (string.IsNullOrEmpty(slot.Value.ToString())) continue;
                    hasWorn = true;
                    GUILayout.Label("• " + WearSlotName(slot.Name) + ": " + Name(slot.Value.ToString()));
                }
            }
            if (!hasWorn) GUILayout.Label("Пока ничего не надето.");
            foreach (var item in new[] { "cloak", "boots", "hat", "jerkin", "shield" })
            {
                var quantity = Count(items, item);
                if (quantity < 1f) continue;
                var equipped = false;
                if (worn != null)
                    foreach (var slot in worn.Properties())
                        if (slot.Value.ToString() == item) equipped = true;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Name(item) + " ×" + (int)quantity, GUILayout.Width(190f));
                GUI.enabled = !equipped;
                if (GUILayout.Button(equipped ? "Надето" : "Надеть"))
                    Ask("Одежда", "wear", "item", item);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            GUILayout.Label("ВЕЩИ В СУМЕ");
            GUILayout.BeginVertical(GUI.skin.box);
            var hasItems = false;
            if (items != null)
            {
                foreach (var item in items.Properties())
                {
                    var quantity = (int)Num(items, item.Name);
                    if (quantity < 1) continue;
                    hasItems = true;
                    GUILayout.Label(Name(item.Name) + " ×" + quantity);
                }
            }
            if (!hasItems) GUILayout.Label("Сума пуста.");
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            GUILayout.Label("ЕДА И ЛЕЧЕНИЕ");
            GUILayout.BeginVertical(GUI.skin.box);
            var hasConsumables = false;
            foreach (var item in new[] { "stew", "roast", "potion", "bandage" })
            {
                var quantity = Count(items, item);
                if (quantity < 1f) continue;
                hasConsumables = true;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Name(item) + " ×" + (int)quantity, GUILayout.Width(190f));
                if (GUILayout.Button(item == "potion" || item == "bandage" ? "Применить" : "Употребить"))
                    Ask("Употребить", "use", "item", item);
                GUILayout.EndHorizontal();
            }
            foreach (var good in new[] { "bread", "fish", "meat", "cheese", "veg", "game", "egg" })
            {
                var quantity = Count(stash, good);
                if (quantity < 1f) continue;
                hasConsumables = true;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Name(good) + " ×" + (int)quantity, GUILayout.Width(190f));
                if (GUILayout.Button("Съесть"))
                    Ask("Еда", "use", "item", good);
                GUILayout.EndHorizontal();
            }
            if (!hasConsumables) GUILayout.Label("Еды и лекарств пока нет.");
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            GUILayout.Label("ВМЕСТИМОСТЬ И ПОКЛАЖА");
            GUILayout.BeginVertical(GUI.skin.box);
            if (bag != null)
            {
                var load = Num(bag, "load");
                var capacity = Num(bag, "cap");
                GUILayout.Label("Сума: " + load.ToString("0.#", CultureInfo.InvariantCulture) + " / "
                    + capacity.ToString("0.#", CultureInfo.InvariantCulture)
                    + (Flag(bag, "over") ? " — перегруз, движение замедлено" : ""));
            }
            else GUILayout.Label("Сведения о вместимости сумы пока недоступны.");

            var horseBag = Obj(horse, "bag");
            var horseHere = Flag(horse, "here");
            if (horse != null)
            {
                GUILayout.Label("Лошадь: " + Str(horse, "name") + (horseHere ? "" : " · в другом поселении"));
                GUILayout.Label("Седло: " + (int)Num(horse, "bag_load") + " / " + (int)Num(horse, "bag_cap"));
                if (horseHere)
                {
                    if (stash != null)
                    {
                        foreach (var packed in stash.Properties())
                        {
                            if (Num(stash, packed.Name) < 1f) continue;
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(Name(packed.Name) + " ×" + (int)Num(stash, packed.Name), GUILayout.Width(190f));
                            if (GUILayout.Button("Уложить в седло"))
                                Ask("Вьюк", "horse", "action", "stow", "good", packed.Name, "qty", 1);
                            GUILayout.EndHorizontal();
                        }
                    }
                    if (horseBag != null)
                    {
                        foreach (var packed in horseBag.Properties())
                        {
                            if (Num(horseBag, packed.Name) < 1f) continue;
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(Name(packed.Name) + " ×" + (int)Num(horseBag, packed.Name), GUILayout.Width(190f));
                            if (GUILayout.Button("Забрать"))
                                Ask("Вьюк", "horse", "action", "unstow", "good", packed.Name, "qty", 1);
                            GUILayout.EndHorizontal();
                        }
                    }
                }
            }
            else GUILayout.Label("Седельной поклажи нет.");
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            GUILayout.Label("Изменения подтверждаются сервером. Разделение стопок и выбрасывание предметов пока не поддерживаются серверными действиями.");
        }

        private static string WearSlotName(string slot)
        {
            switch (slot)
            {
                case "head": return "Голова";
                case "body": return "Тело";
                case "feet": return "Ноги";
                case "armor": return "Броня";
                case "offhand": return "Вторая рука";
                default: return slot;
            }
        }

        public void SetContext(bool active, bool firstPerson, bool hudCollapsed, Transform player,
            PresentationCameraRig rig)
        {
            if (_active && !active) Clear();
            _active = active;
            _firstPerson = firstPerson;
            _hudCollapsed = hudCollapsed;
            _player = player;
            _rig = rig;
        }

        /// <summary>Fast frames (?fast=N) carry only the body, combat state, feed and foes.</summary>
        public static bool IsFastFrame(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            var head = json.Length > 64 ? json.Substring(0, 64) : json;
            return head.IndexOf("\"fast\":true", StringComparison.Ordinal) >= 0;
        }

        public void ApplyFrame(string json)
        {
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception) { return; }
            _me = Obj(root, "me");
            _place = Obj(root, "place");
            if (root["hour"] != null) _hour = (int)Num(root, "hour");
            SyncFoes(Arr(_place, "foes"));
            SyncExits(Arr(_place, "exits"));
            ApplySitePresentation();
            ReadFeed(Arr(_me, "feed"));
            ReadResult();
        }

        public void ApplyFastFrame(string json)
        {
            if (_me == null) return;
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception) { return; }
            var me = Obj(root, "me");
            if (me != null)
            {
                foreach (var property in me.Properties())
                    _me[property.Name] = property.Value;
            }
            var place = Obj(root, "place");
            if (place != null)
            {
                _place = place;
                SyncFoes(Arr(place, "foes"));
                SyncExits(Arr(place, "exits"));
                ApplySitePresentation();
            }
            ReadFeed(Arr(_me, "feed"));
            ReadResult();
        }

        public void Clear()
        {
            foreach (var pair in _foes)
                if (pair.Value.Root != null) Destroy(pair.Value.Root);
            _foes.Clear();
            foreach (var view in _exitViews.Values) if (view != null) Destroy(view);
            _exitViews.Clear();
            _feed.Clear();
            _floaters.Clear();
            if (_gate != null) _gate.Clear();
            _toast = null;
            _fishPhase = 0;
            _chopSwings = 0;
            _feedSeq = 0;
            _me = null;
            _place = null;
            _riderVisualRoot = null;
            _riderBody = _riderHead = _riderLeftArm = _riderRightArm = _riderLeftLeg = _riderRightLeg = null;
            _riderHomePositions = null;
            _riderHomeRotations = null;
            _riderHomeCaptured = false;
            _menuOpen = false;
            CloseWeave(false);
            _holding = false;
            _guardSent = false;
            if (_handLight != null) _handLight.intensity = 0f;
            if (_mount != null) _mount.SetActive(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // ------------------------------------------------------------------ JSON helpers

        private static JToken Tok(JToken token, string key)
        {
            var obj = token as JObject;
            if (obj == null) return null;
            JToken value;
            if (!obj.TryGetValue(key, out value) || value == null || value.Type == JTokenType.Null) return null;
            return value;
        }

        private static string Str(JToken token, string key)
        {
            var value = Tok(token, key);
            return value == null ? string.Empty : value.ToString();
        }

        private static float Num(JToken token, string key)
        {
            var value = Tok(token, key);
            if (value == null) return 0f;
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return value.Value<float>();
            float parsed;
            return float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed : 0f;
        }

        private static bool Flag(JToken token, string key)
        {
            var value = Tok(token, key);
            return value != null && value.Type == JTokenType.Boolean && value.Value<bool>();
        }

        private static JObject Obj(JToken token, string key)
        {
            return Tok(token, key) as JObject;
        }

        private static JArray Arr(JToken token, string key)
        {
            return Tok(token, key) as JArray;
        }

        private static string Name(string key)
        {
            string value;
            return !string.IsNullOrEmpty(key) && Ru.TryGetValue(key, out value) ? value : (key ?? string.Empty);
        }

        private static Dictionary<string, object> Payload(string op, object[] pairs)
        {
            var payload = new Dictionary<string, object> { { "v", 1 }, { "op", op } };
            for (var i = 0; i + 1 < pairs.Length; i += 2)
                payload[(string)pairs[i]] = pairs[i + 1];
            return payload;
        }

        /// <summary>
        /// Fire-and-forget intent (attack, guard, dodge, stance, tricks, road choice). It carries NO reqid on
        /// purpose: the server then leaves the single me.result slot alone, so these frequent inputs cannot
        /// overwrite the acknowledgement of a request that is waiting in the gate. Their outcome is read from
        /// confirmed state (me.feed, me.combat, place.foes).
        /// </summary>
        private void Send(string op, params object[] pairs)
        {
            if (_send == null) return;
            _send(Payload(op, pairs));
        }

        /// <summary>
        /// Result-bearing intent. Nothing is shown as done here. Requests go through the gate one at a time:
        /// the next is sent only after me.result with the matching reqid arrived or the wait timed out.
        /// </summary>
        /// <summary>
        /// Public entry point for other client layers (notably NPC talk): every action that expects
        /// me.result must share this one gate. Returns false when disconnected or the bounded queue is full.
        /// </summary>
        public bool QueueRequest(string label, string op, params object[] pairs)
        {
            if (_send == null) return false;
            if (_gate == null) _gate = new RequestGate(_send, PendingTimeout, 6);
            var payload = Payload(op, pairs);
            var reqid = Guid.NewGuid().ToString("N");
            payload["reqid"] = reqid;
            return _gate.Enqueue(label, reqid, payload, Time.unscaledTime);
        }

        private void Ask(string label, string op, params object[] pairs)
        {
            if (!QueueRequest(label, op, pairs))
                Toast("Не всё сразу: дождись ответа на прошлые действия", 1.6f);
        }

        private void ReadResult()
        {
            var result = Obj(_me, "result");
            if (result == null || _gate == null) return;
            RequestGate.Item item;
            var reason = Str(result, "reason");
            var outcome = _gate.OnResult(Str(result, "reqid"), Flag(result, "ok"), Time.unscaledTime, out item);
            if (outcome == RequestGate.Outcome.Accepted || outcome == RequestGate.Outcome.Rejected)
                RequestResolved?.Invoke(item.Label, outcome, reason);
            switch (outcome)
            {
                case RequestGate.Outcome.Accepted:
                    Toast(item.Label + ": сделано", 1.4f);
                    break;
                case RequestGate.Outcome.Rejected:
                    Toast(item.Label + " — отказ: " + reason, 2.6f);
                    break;
            }
        }

        private void ExpirePending()
        {
            if (_gate == null) return;
            RequestGate.Item item;
            if (_gate.Tick(Time.unscaledTime, out item) == RequestGate.Outcome.TimedOut)
            {
                RequestResolved?.Invoke(item.Label, RequestGate.Outcome.TimedOut, "сервер не ответил");
                Toast(item.Label + ": сервер не ответил", 2.2f);
            }
        }

        // ------------------------------------------------------------------ foes

        private void SyncFoes(JArray foes)
        {
            _seen.Clear();
            if (foes != null)
            {
                foreach (var token in foes)
                {
                    var id = Str(token, "id");
                    if (string.IsNullOrEmpty(id)) continue;
                    _seen.Add(id);
                    FoeRec rec;
                    if (!_foes.TryGetValue(id, out rec))
                    {
                        rec = CreateFoe(id, Str(token, "kind"), Str(token, "name"));
                        rec.Root.transform.position = new Vector3(Num(token, "x"), 0f, Num(token, "y"));
                        _foes[id] = rec;
                    }
                    rec.Target = new Vector3(Num(token, "x"), 0f, Num(token, "y"));
                    rec.Yaw = EntityViewRegistry.FacingToUnityYaw(Num(token, "facing"));
                    rec.Hp = Num(token, "hp");
                    rec.Max = Mathf.Max(1f, Num(token, "max"));
                    rec.Aware = Flag(token, "aware");
                    rec.State = Str(token, "state");
                    if (rec.Gait != null)
                    {
                        rec.Gait.Dead = rec.State == "dead";
                        rec.Gait.Chasing = rec.State == "chase";
                        rec.Gait.Eating = rec.State == "eat";
                    }
                    rec.View.Dead = rec.State == "dead";
                }
            }

            _stale.Clear();
            foreach (var pair in _foes)
                if (!_seen.Contains(pair.Key)) _stale.Add(pair.Key);
            foreach (var id in _stale)
            {
                if (_foes[id].Root != null) Destroy(_foes[id].Root);
                _foes.Remove(id);
            }
        }

        // Settlement exits are authoritative scene anchors from place.exits. The client only
        // decorates these exact points; it never invents a destination or enables op:"travel".
        private void SyncExits(JArray exits)
        {
            var active = new HashSet<string>();
            if (exits != null)
            {
                foreach (var token in exits)
                {
                    var id = Str(token, "to");
                    if (string.IsNullOrEmpty(id)) continue;
                    active.Add(id);
                    GameObject root;
                    if (!_exitViews.TryGetValue(id, out root) || root == null)
                    {
                        root = BuildExitView(id);
                        _exitViews[id] = root;
                    }

                    var x = Num(token, "x");
                    var z = Num(token, "y");
                    root.transform.position = new Vector3(x, 0f, z);
                    var inward = new Vector3(-x, 0f, -z);
                    if (inward.sqrMagnitude < 0.01f) inward = Vector3.forward;
                    root.transform.rotation = Quaternion.LookRotation(inward.normalized, Vector3.up);
                    var pass = Str(token, "kind") == "pass";
                    var label = Str(token, "name");
                    if (string.IsNullOrEmpty(label)) label = pass ? "Перевал" : "Тракт";
                    var text = root.GetComponentInChildren<TextMesh>();
                    if (text != null) text.text = label;
                    var marker = root.transform.Find("Pass marker");
                    if (marker != null) marker.gameObject.SetActive(pass);
                }
            }

            var stale = new List<string>();
            foreach (var pair in _exitViews)
                if (!active.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (var id in stale)
            {
                if (_exitViews[id] != null) Destroy(_exitViews[id]);
                _exitViews.Remove(id);
            }
        }

        private GameObject BuildExitView(string destinationId)
        {
            var root = new GameObject("Settlement exit " + destinationId);
            root.transform.SetParent(transform, true);
            var wood = new Color(0.27f, 0.17f, 0.1f);
            var pale = new Color(0.72f, 0.61f, 0.42f);
            CreateExitPart(root.transform, new Vector3(-1.15f, 1.35f, 0f), new Vector3(0.22f, 2.7f, 0.24f), wood);
            CreateExitPart(root.transform, new Vector3(1.15f, 1.35f, 0f), new Vector3(0.22f, 2.7f, 0.24f), wood);
            CreateExitPart(root.transform, new Vector3(0f, 2.62f, 0f), new Vector3(2.5f, 0.24f, 0.28f), wood);
            CreateExitPart(root.transform, new Vector3(0f, 0.045f, -2.4f), new Vector3(2.3f, 0.08f, 4.8f), new Color(0.34f, 0.28f, 0.19f));
            var marker = CreateExitPart(root.transform, new Vector3(0f, 3.05f, 0f), new Vector3(0.34f, 0.55f, 0.34f), pale);
            marker.name = "Pass marker";
            marker.gameObject.SetActive(false);

            var labelObject = new GameObject("Exit label");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 3.35f, 0.08f);
            var mesh = labelObject.AddComponent<TextMesh>();
            mesh.text = "Тракт";
            mesh.fontSize = 48;
            mesh.characterSize = 0.075f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.94f, 0.87f, 0.68f);
            foreach (var collider in root.GetComponentsInChildren<Collider>()) Destroy(collider);
            return root;
        }

        private static Transform CreateExitPart(Transform parent, Vector3 localPosition, Vector3 scale, Color color)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            var renderer = part.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (renderer != null && shader != null)
            {
                var material = new Material(shader);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                else if (material.HasProperty("_Color")) material.color = color;
                renderer.sharedMaterial = material;
            }
            var collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return part.transform;
        }

        private FoeRec CreateFoe(string id, string kind, string displayName)
        {
            if (_foeRoot == null)
            {
                _foeRoot = new GameObject("CoreFoes").transform;
                _foeRoot.SetParent(transform, false);
            }

            var root = new GameObject("Foe " + kind + " " + id);
            root.transform.SetParent(_foeRoot, false);
            var view = root.AddComponent<CoreFoeView>();
            view.FoeId = id;
            view.Kind = kind;
            view.DisplayName = string.IsNullOrEmpty(displayName) ? kind : displayName;

            _surface = kind == "bandit" ? "cloth" : "fur";
            if (kind == "bandit")
            {
                view.Humanoid = true;
                view.Height = 1.9f;
                var cloth = new Color(0.35f, 0.12f, 0.10f);
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.4f, 0f), new Vector3(0.42f, 0.4f, 0.42f), new Color(0.2f, 0.17f, 0.14f));
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 1.15f, 0f), new Vector3(0.58f, 0.37f, 0.58f), cloth);
                Part(root.transform, PrimitiveType.Sphere, new Vector3(0f, 1.7f, 0f), new Vector3(0.4f, 0.4f, 0.4f), new Color(0.3f, 0.2f, 0.16f));
                Part(root.transform, PrimitiveType.Cube, new Vector3(0.36f, 1.05f, 0.3f), new Vector3(0.07f, 0.07f, 0.9f), new Color(0.4f, 0.3f, 0.2f));
                // hood, arms, belt and eyes: a readable silhouette from a distance
                Part(root.transform, PrimitiveType.Sphere, new Vector3(0f, 1.76f, -0.04f), new Vector3(0.48f, 0.42f, 0.48f), new Color(0.16f, 0.12f, 0.1f));
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(-0.36f, 1.12f, 0f), new Vector3(0.14f, 0.32f, 0.14f), cloth);
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0.36f, 1.12f, 0f), new Vector3(0.14f, 0.32f, 0.14f), cloth);
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.86f, 0f), new Vector3(0.6f, 0.04f, 0.6f), new Color(0.12f, 0.09f, 0.07f));
                Part(root.transform, PrimitiveType.Sphere, new Vector3(-0.07f, 1.72f, 0.18f), new Vector3(0.05f, 0.05f, 0.05f), Color.white);
                Part(root.transform, PrimitiveType.Sphere, new Vector3(0.07f, 1.72f, 0.18f), new Vector3(0.05f, 0.05f, 0.05f), Color.white);
            }
            else
            {
                float len, height, width;
                Color color;
                switch (kind)
                {
                    case "hare": len = 0.45f; height = 0.28f; width = 0.22f; color = new Color(0.79f, 0.72f, 0.6f); break;
                    case "fox": len = 0.8f; height = 0.4f; width = 0.26f; color = new Color(0.78f, 0.4f, 0.16f); break;
                    case "boar": len = 1.3f; height = 0.75f; width = 0.55f; color = new Color(0.29f, 0.23f, 0.17f); break;
                    case "bear": len = 2.0f; height = 1.25f; width = 0.95f; color = new Color(0.23f, 0.16f, 0.12f); break;
                    default: len = 1.3f; height = 0.7f; width = 0.4f; color = new Color(0.49f, 0.5f, 0.53f); break;
                }
                var leg = height * 0.45f;
                view.Length = len;
                view.LegHeight = leg;
                view.Height = leg + height * 0.8f;
                Part(root.transform, PrimitiveType.Cube, new Vector3(0f, leg + height * 0.3f, 0f), new Vector3(width, height * 0.6f, len), color);
                Part(root.transform, PrimitiveType.Cube, new Vector3(0f, leg + height * 0.5f, len * 0.62f), new Vector3(width * 0.75f, height * 0.45f, len * 0.32f), color);
                var dark = new Color(0.17f, 0.14f, 0.11f);
                var gait = root.AddComponent<ProceduralGait>();
                gait.LegLength = leg;
                gait.StrideDegrees = kind == "hare" ? 40f : kind == "bear" ? 20f : 28f;
                for (var sz = 1; sz >= -1; sz -= 2)
                    for (var sx = -1; sx <= 1; sx += 2)
                        gait.Legs.Add(Part(root.transform, PrimitiveType.Cube, new Vector3(sx * width * 0.36f, leg * 0.5f, sz * len * 0.36f), new Vector3(width * 0.22f, leg, width * 0.22f), dark));
                gait.Body = root.transform.GetChild(0);
                gait.Head = root.transform.GetChild(1);
                // species details: snout, ears, tail, eyes, tusks, hump — enough to tell them apart at a glance
                var headY = leg + height * 0.5f;
                var headZ = len * 0.62f;
                Part(root.transform, PrimitiveType.Cube, new Vector3(0f, headY - height * 0.08f, headZ + len * 0.2f), new Vector3(width * 0.4f, height * 0.22f, len * 0.16f), dark);
                var earHeight = kind == "hare" ? height * 0.9f : height * 0.25f;
                for (var side = -1; side <= 1; side += 2)
                {
                    Part(root.transform, PrimitiveType.Cube, new Vector3(side * width * 0.24f, headY + height * 0.22f + earHeight * 0.5f, headZ - len * 0.06f), new Vector3(width * 0.14f, earHeight, width * 0.1f), color);
                    Part(root.transform, PrimitiveType.Sphere, new Vector3(side * width * 0.2f, headY + height * 0.08f, headZ + len * 0.15f), new Vector3(0.05f, 0.05f, 0.05f), kind == "wolf" ? new Color(0.95f, 0.8f, 0.2f) : Color.black);
                }
                if (kind == "boar")
                    for (var side = -1; side <= 1; side += 2)
                        Part(root.transform, PrimitiveType.Cube, new Vector3(side * width * 0.2f, headY - height * 0.12f, headZ + len * 0.24f), new Vector3(0.04f, 0.14f, 0.04f), new Color(0.92f, 0.9f, 0.82f));
                if (kind == "bear")
                    Part(root.transform, PrimitiveType.Sphere, new Vector3(0f, leg + height * 0.62f, len * 0.18f), new Vector3(width * 0.9f, height * 0.5f, len * 0.5f), color);
                var tailLength = kind == "fox" || kind == "wolf" ? len * 0.45f : len * 0.12f;
                var tailColor = kind == "hare" ? Color.white : color;
                gait.Tail = Part(root.transform, PrimitiveType.Cube, new Vector3(0f, leg + height * 0.42f, -len * 0.5f - tailLength * 0.5f), new Vector3(width * 0.2f, width * 0.2f, tailLength), tailColor);
            }

            return new FoeRec { Root = root, View = view, Gait = root.GetComponent<ProceduralGait>() };
        }

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private string _surface = "hide"; // which procedural detail texture the next parts get

        private Transform Part(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            var part = GameObject.CreatePrimitive(type); // keeps its collider: the aim ray needs it
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var renderer = part.GetComponent<Renderer>();
            if (renderer == null) return part.transform;
            if (_block == null) _block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            var detail = ProceduralTextures.Get(_surface); // grain multiplied by the base colour
            _block.SetTexture(BaseMapId, detail);
            _block.SetTexture(MainTexId, detail);
            renderer.SetPropertyBlock(_block);
            return part.transform;
        }

        // ------------------------------------------------------------------ feed

        private void ReadFeed(JArray feed)
        {
            if (feed == null) return;
            foreach (var token in feed)
            {
                var seq = (long)Num(token, "seq");
                if (seq <= _feedSeq) continue;
                _feedSeq = seq;
                var kind = Str(token, "kind");
                var text = Str(token, "text");
                var damage = Num(token, "dmg");
                if (damage > 0f) text += " (" + Mathf.RoundToInt(damage) + ")";
                _feed.Add(new FeedLine { Kind = kind, Text = text, At = Time.unscaledTime });
                if (kind == "hurt" || kind == "backfire" || kind == "ko") _hurtFlash = kind == "ko" ? 1.6f : 0.35f;
                if (kind == "hit" || kind == "kill") _hitMarker = 0.18f;
                FoeRec struck;
                var foeId = Str(token, "foe");
                if (damage > 0f && !string.IsNullOrEmpty(foeId) && _foes.TryGetValue(foeId, out struck) && struck.Root != null)
                {
                    var hurtsMe = kind == "hurt";
                    var anchor = hurtsMe && _player != null ? _player.position + Vector3.up * 1.4f
                        : struck.Root.transform.position + Vector3.up * (struck.View.Height + 0.2f);
                    _floaters.Add(new Floater { World = anchor, Text = (hurtsMe ? "-" : "") + Mathf.RoundToInt(damage), At = Time.unscaledTime, Hurt = hurtsMe });
                    if (!hurtsMe) struck.Punch = 0.2f;
                    else if (struck.Gait != null) struck.Gait.Lunge(); // the server confirmed this foe's hit
                }
            }
            while (_feed.Count > 7) _feed.RemoveAt(0);
        }

        // ------------------------------------------------------------------ update / input

        private void Update()
        {
            if (!_active || _me == null)
            {
                return;
            }

            var blend = 1f - Mathf.Exp(-9f * Time.deltaTime);
            foreach (var pair in _foes)
            {
                var rec = pair.Value;
                if (rec.Root == null) continue;
                var t = rec.Root.transform;
                t.position = Vector3.Lerp(t.position, rec.Target, blend);
                var target = rec.View.Dead
                    ? Quaternion.Euler(0f, rec.Yaw, 90f)
                    : Quaternion.Euler(0f, rec.Yaw, 0f);
                t.rotation = Quaternion.Slerp(t.rotation, target, blend);
                rec.Punch = Mathf.Max(0f, rec.Punch - Time.deltaTime);
                var squash = 1f - Mathf.Sin(Mathf.Clamp01(rec.Punch / 0.2f) * Mathf.PI) * 0.18f;
                t.localScale = new Vector3(1f / squash, squash, 1f / squash);
            }
            while (_floaters.Count > 0 && Time.unscaledTime - _floaters[0].At > 1.1f) _floaters.RemoveAt(0);
            ExpirePending();
            if (_fishPhase == 1 && Time.unscaledTime >= _fishAt)
            {
                _fishPhase = 2;
                _fishAt = Time.unscaledTime + 1.2f;
                Toast("КЛЮЁТ — ЛКМ!", 1.2f);
            }
            else if (_fishPhase == 2 && Time.unscaledTime >= _fishAt)
            {
                _fishPhase = 0;
                Toast("Ушла…", 1.2f);
            }

            _hurtFlash = Mathf.Max(0f, _hurtFlash - Time.unscaledDeltaTime);
            _hitMarker = Mathf.Max(0f, _hitMarker - Time.unscaledDeltaTime);
            while (_feed.Count > 0 && Time.unscaledTime - _feed[0].At > 10f) _feed.RemoveAt(0);

            var journey = Obj(_me, "journey");
            var talking = Obj(_me, "talk") != null;
            UpdateHandLight();
            UpdateMount();

            if (!Application.isFocused)
            {
                // Focus loss cancels held input locally; never leave guard/attack/weave latched.
                _holding = false;
                _holdStart = 0f;
                _fishPhase = 0;
                ReleaseGuard();
                if (_weaveOpen) CloseWeave(false);
                UpdateCursor(true);
                return;
            }

            if (journey != null)
            {
                _menuOpen = false;
                CloseWeave(false);
                ReleaseGuard();
                var roadEvent = Obj(journey, "event");
                if (roadEvent != null && !Flag(journey, "follow"))
                {
                    var options = Arr(roadEvent, "options");
                    for (var i = 0; options != null && i < options.Count && i < 9; i++)
                        if (_input != null && _input.JourneyChoicePressed(i))
                            Ask("Выбор пути", "journey", "action", "choose", "choice", options[i].ToString());
                }
                UpdateCursor(true);
                return;
            }

            if (_input != null && _input.MenuPressed)
            {
                _menuOpen = !_menuOpen;
                if (_menuOpen) CloseWeave(false);
            }
            if (_input != null && _input.CancelPressed)
            {
                _menuOpen = false;
                CloseWeave(false);
            }

            if (!_menuOpen && !talking)
            {
                if (_input != null && _input.WeavePressed) OpenWeave();
                if (_input != null && _input.WeaveReleased && _weaveOpen) CloseWeave(true);
            }

            if (_weaveOpen)
            {
                UpdateWeave();
            }
            else if (!_menuOpen && !talking)
            {
                UpdateCombatInput();
            }
            else
            {
                _holding = false;
                ReleaseGuard();
            }

            UpdateCursor(_menuOpen || _weaveOpen || talking);
        }

        private void UpdateCursor(bool needsPointer)
        {
            // First person with the HUD folded away is the "immersive" state: the pointer is captured
            // and the crosshair aims. Any window that needs clicking releases it.
            var capture = _firstPerson && _hudCollapsed && !needsPointer;
            Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !capture;
        }

        private bool PointerOverHud()
        {
            if (GUIUtility.hotControl != 0) return true;
            if (Cursor.lockState == CursorLockMode.Locked) return false;
            var mouse = _input != null ? _input.PointerPosition : Vector2.zero;
            var guiY = Screen.height - mouse.y;
            if (!_hudCollapsed && mouse.x < 450f && guiY < 530f) return true;   // main panel
            if (_hudCollapsed && mouse.x < 170f && guiY < 46f) return true;      // "show menu" button
            if (mouse.x > Screen.width - 440f && guiY > Screen.height - 270f) return true; // talk overlay
            return false;
        }

        private void UpdateCombatInput()
        {
            if (_input != null && _input.PrimaryPressed && !PointerOverHud())
            {
                _holding = true;
                _holdStart = Time.unscaledTime;
            }
            if (_input != null && _input.PrimaryReleased && _holding)
            {
                _holding = false;
                Attack(Time.unscaledTime - _holdStart, false);
            }

            if (_input != null && _input.SecondaryPressed && !PointerOverHud())
            {
                Send("guard", "on", true);
                _guardSent = true;
            }
            if (_input != null && _input.SecondaryReleased) ReleaseGuard();

            if (_input != null && _input.ToggleHintsPressed) _showHints = !_showHints;
            if (_input != null && _input.DodgePressed) Send("dodge");
            if (_input != null && _input.QuickAttackPressed) Attack(0f, true);
            if (_input != null && _input.TrickSandPressed) Send("trick", "trick", "sand");
            if (_input != null && _input.TrickShovePressed) Send("trick", "trick", "shove");
            if (_input != null && _input.TrickFeintPressed) Send("trick", "trick", "feint");
            if (_input != null && _input.TrickBaitPressed) Send("trick", "trick", "bait");
            if (_input != null && _input.TrickSneakPressed) Send("trick", "trick", "sneak");
            if (_input != null && _input.CycleStancePressed)
            {
                var stance = Str(Obj(_me, "combat"), "stance");
                var next = stance == "balanced" ? "aggressive" : stance == "aggressive" ? "defensive" : "balanced";
                Send("stance", "stance", next);
            }
        }

        private void ReleaseGuard()
        {
            if (!_guardSent) return;
            _guardSent = false;
            Send("guard", "on", false);
        }

        private bool AimFoe(out CoreFoeView view, out string part, out float accuracy, out float distance)
        {
            view = null;
            part = "torso";
            accuracy = 0f;
            distance = 0f;
            var cam = Camera.main;
            if (cam == null) return false;
            var ray = _firstPerson || Cursor.lockState == CursorLockMode.Locked
                ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : cam.ScreenPointToRay(_input != null ? _input.PointerPosition : Vector2.zero);
            var hits = Physics.RaycastAll(ray, 140f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (_player != null && hit.collider.transform.IsChildOf(_player)) continue; // own body
                var foe = hit.collider.GetComponentInParent<CoreFoeView>();
                if (foe == null) return false; // a wall or a villager is in the way
                if (foe.Dead) continue;
                var local = foe.transform.InverseTransformPoint(hit.point);
                var half = 0.3f;
                if (foe.Humanoid)
                {
                    if (local.y > 1.48f) part = "head";
                    else if (local.y < 0.8f) part = "leg";
                    else if (Mathf.Abs(local.x) > 0.22f) part = "arm";
                }
                else
                {
                    half = Mathf.Max(0.1f, foe.Length * 0.3f);
                    if (local.z > foe.Length * 0.42f) part = "head";
                    else if (local.y < foe.LegHeight) part = "leg";
                }
                accuracy = Mathf.Clamp(1f - 0.4f * Mathf.Abs(local.x) / half, 0.55f, 1f);
                var origin = _player != null ? _player.position : cam.transform.position;
                var flat = foe.transform.position - origin;
                flat.y = 0f;
                distance = flat.magnitude;
                view = foe;
                return true;
            }
            return false;
        }

        private void Attack(float held, bool thrust)
        {
            var combat = Obj(_me, "combat");
            var ranged = Str(combat, "weapon") == "bow";
            CoreFoeView foe;
            string part;
            float accuracy, distance;
            var aimed = AimFoe(out foe, out part, out accuracy, out distance);
            var weapon = Str(combat, "weapon");
            var pos = Obj(_me, "pos");
            if (!aimed && weapon == "axe" && pos != null && (Num(pos, "x") <= -70f || Near("woods", 8f)))
            {
                // Three swings fell a log; the server still checks place and strength for the real outcome.
                _chopSwings++;
                if (_chopSwings >= 3)
                {
                    _chopSwings = 0;
                    Ask("Рубка", "act", "action", "chop");
                }
                else Toast("Тюк… (" + _chopSwings + "/3)", 0.8f);
                return;
            }
            if (!aimed && weapon == "rod" && pos != null && Num(pos, "y") >= -84f && Num(pos, "y") <= -72f)
            {
                if (_fishPhase == 0)
                {
                    _fishPhase = 1;
                    _fishAt = Time.unscaledTime + UnityEngine.Random.Range(1.8f, 5f);
                    Toast("Заброс… жди поклёвки", 1.6f);
                }
                else if (_fishPhase == 2)
                {
                    _fishPhase = 0;
                    Ask("Подсечка", "act", "action", "fish");
                }
                else
                {
                    _fishPhase = 0;
                    Toast("Рано дёрнул — сорвалось", 1.2f);
                }
                return;
            }
            var style = ranged ? (held < 0.35f ? "snap" : "drawn") : thrust ? "thrust" : held > 0.55f ? "heavy" : "slash";
            var reach = ranged ? 50f : 3.0f;

            if (_firstPerson)
            {
                // The player aimed: report target, body part and accuracy; the server verifies range and cone.
                if (aimed && distance <= reach)
                    Send("attack", "aimed", true, "target", foe.FoeId, "aim", part, "acc", accuracy, "style", style,
                        "power", ranged ? Mathf.Clamp(held / 1.1f, 0.2f, 1f) : 0f);
                else
                    Send("attack", "aimed", true, "aim", "torso", "acc", 0f, "style", style);
            }
            else if (aimed)
            {
                // Third person: the click only picks the target; the server faces it and rolls dice.
                Send("attack", "target", foe.FoeId, "aim", part, "style", style);
            }
            else
            {
                Send("attack", "aim", "torso", "style", style);
            }
        }

        private void Toast(string text, float seconds)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + seconds;
        }

        private GameObject _mount;
        private string _lastSiteSignature;
        private Transform _riderVisualRoot;
        private Transform _riderBody, _riderHead, _riderLeftArm, _riderRightArm, _riderLeftLeg, _riderRightLeg;
        private Vector3[] _riderHomePositions;
        private Quaternion[] _riderHomeRotations;
        private bool _riderHomeCaptured;

        // Procedural low-poly horse: anatomical silhouette, articulated legs, mane, tack and saddle.
        // It remains presentation-only; ownership, stamina, inventory and mounting are server-owned.
        private void UpdateMount()
        {
            var horse = Obj(_me, "horse");
            var riding = Flag(horse, "mounted") && _player != null;
            if (!riding)
            {
                if (_mount != null) _mount.SetActive(false);
                return;
            }
            if (_mount == null)
            {
                _mount = new GameObject("Player mount • procedural horse");
                _mount.transform.SetParent(transform, true);
                _surface = "fur";
                var coat = new Color(0.43f, 0.25f, 0.14f);
                var highlight = new Color(0.57f, 0.35f, 0.21f);
                var mane = new Color(0.12f, 0.085f, 0.065f);
                var leather = new Color(0.19f, 0.12f, 0.075f);
                var blanket = new Color(0.48f, 0.23f, 0.15f);
                var hoof = new Color(0.12f, 0.105f, 0.09f);

                var body = Part(_mount.transform, PrimitiveType.Capsule, new Vector3(0f, 1.15f, -0.05f), new Vector3(0.58f, 0.91f, 1.75f), coat);
                body.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var chest = Part(_mount.transform, PrimitiveType.Sphere, new Vector3(0f, 1.18f, 0.68f), new Vector3(0.62f, 0.88f, 0.62f), highlight);
                var rump = Part(_mount.transform, PrimitiveType.Sphere, new Vector3(0f, 1.18f, -0.68f), new Vector3(0.64f, 0.82f, 0.68f), coat);
                var neck = Part(_mount.transform, PrimitiveType.Capsule, new Vector3(0f, 1.72f, 0.72f), new Vector3(0.34f, 0.72f, 0.38f), coat);
                neck.localRotation = Quaternion.Euler(-27f, 0f, 0f);
                var head = Part(_mount.transform, PrimitiveType.Capsule, new Vector3(0f, 2.08f, 1.14f), new Vector3(0.24f, 0.42f, 0.27f), highlight);
                head.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                Part(_mount.transform, PrimitiveType.Sphere, new Vector3(0f, 1.95f, 1.37f), new Vector3(0.22f, 0.2f, 0.32f), coat);
                for (var side = -1; side <= 1; side += 2)
                {
                    Part(_mount.transform, PrimitiveType.Cube, new Vector3(side * 0.13f, 2.31f, 1.08f), new Vector3(0.075f, 0.3f, 0.09f), mane);
                    Part(_mount.transform, PrimitiveType.Sphere, new Vector3(side * 0.125f, 2.12f, 1.29f), new Vector3(0.045f, 0.05f, 0.035f), Color.black);
                    Part(_mount.transform, PrimitiveType.Sphere, new Vector3(side * 0.19f, 2.02f, 1.45f), new Vector3(0.07f, 0.05f, 0.08f), mane);
                }
                // Mane follows the crest; tail hangs behind the rump.
                for (var i = 0; i < 5; i++)
                    Part(_mount.transform, PrimitiveType.Capsule, new Vector3(0f, 2.12f - i * 0.11f, 1.0f - i * 0.1f), new Vector3(0.08f, 0.16f, 0.08f), mane);
                var tail = Part(_mount.transform, PrimitiveType.Capsule, new Vector3(0f, 1.12f, -1.25f), new Vector3(0.11f, 0.42f, 0.13f), mane);
                tail.localRotation = Quaternion.Euler(18f, 0f, 0f);

                // Blanket, saddle, girth, reins and bridle make the animal read as a mount.
                Part(_mount.transform, PrimitiveType.Cube, new Vector3(0f, 1.58f, -0.05f), new Vector3(0.72f, 0.12f, 0.82f), blanket);
                Part(_mount.transform, PrimitiveType.Cube, new Vector3(0f, 1.68f, -0.02f), new Vector3(0.46f, 0.13f, 0.52f), leather);
                Part(_mount.transform, PrimitiveType.Cube, new Vector3(0f, 1.49f, 0.05f), new Vector3(0.66f, 0.055f, 0.12f), leather);
                Part(_mount.transform, PrimitiveType.Cube, new Vector3(0f, 1.83f, 0.92f), new Vector3(0.05f, 0.055f, 0.58f), leather);
                Part(_mount.transform, PrimitiveType.Cube, new Vector3(0f, 1.96f, 1.08f), new Vector3(0.36f, 0.055f, 0.07f), leather);

                var gait = _mount.AddComponent<ProceduralGait>();
                gait.LegLength = 0.82f;
                gait.StrideDegrees = 28f;
                gait.Body = body;
                gait.Head = head;
                gait.Tail = tail;
                for (var sz = 1; sz >= -1; sz -= 2)
                for (var sx = -1; sx <= 1; sx += 2)
                {
                    var hip = new Vector3(sx * 0.22f, 0.83f, sz * 0.62f);
                    var leg = Part(_mount.transform, PrimitiveType.Capsule, hip + Vector3.down * 0.39f,
                        new Vector3(0.13f, 0.43f, 0.13f), coat);
                    gait.Legs.Add(leg);
                    Part(_mount.transform, PrimitiveType.Cube, hip + Vector3.down * 0.79f + Vector3.forward * 0.035f,
                        new Vector3(0.16f, 0.13f, 0.22f), hoof);
                }
                foreach (var collider in _mount.GetComponentsInChildren<Collider>()) Destroy(collider);
            }
            _mount.SetActive(true);
            var facing = Quaternion.Euler(0f, _player.eulerAngles.y, 0f);
            _mount.transform.SetPositionAndRotation(_player.position, facing);
            var stamina = Num(horse, "stam");
            var breath = 1f + Mathf.Sin(Time.time * 2.1f) * 0.008f * (1f - stamina / 100f);
            _mount.transform.localScale = new Vector3(1f, breath, 1f);
        }

        private bool SiteHas(string feature)
        {
            var site = Obj(_place, "site");
            var features = Arr(site, "features");
            if (features == null) return true; // compatibility with older frames
            foreach (var featureToken in features)
                if (string.Equals(featureToken.ToString(), feature, StringComparison.Ordinal)) return true;
            return false;
        }

        private void ApplySitePresentation()
        {
            var site = Obj(_place, "site");
            if (site == null) return;
            var terrain = Str(site, "terrain");
            var climate = Str(site, "climate");
            var signature = terrain + "|" + climate + "|" + SiteHas("river") + "|" + SiteHas("garden");
            if (signature == _lastSiteSignature) return;
            _lastSiteSignature = signature;

            var all = transform.root.GetComponentsInChildren<Transform>(true);
            foreach (var item in all)
            {
                if (item.name == "Village garden") item.gameObject.SetActive(SiteHas("garden"));
                else if (item.name == "Fishing dock" || item.name == "Narrow village creek" ||
                         item.name == "Old plank footbridge" || item.name == "Creek east sloped bank" ||
                         item.name == "Creek west sloped bank" || item.name == "Creek waterline stone")
                    item.gameObject.SetActive(SiteHas("river"));
                else if (item.name == "Meadow")
                {
                    var renderer = item.GetComponent<Renderer>();
                    if (renderer == null) continue;
                    var color = terrain == "marsh" ? new Color(0.48f, 0.49f, 0.36f)
                        : terrain == "coast" ? new Color(0.68f, 0.62f, 0.46f)
                        : terrain == "hills" ? new Color(0.43f, 0.49f, 0.34f)
                        : terrain == "forest" ? new Color(0.34f, 0.43f, 0.29f)
                        : new Color(0.49f, 0.55f, 0.36f);
                    if (climate == "cold" || climate == "subarctic") color = Color.Lerp(color, new Color(0.66f, 0.69f, 0.62f), 0.35f);
                    else if (climate == "dry" || climate == "arid") color = Color.Lerp(color, new Color(0.76f, 0.65f, 0.39f), 0.42f);
                    else if (climate == "wet" || climate == "humid") color = Color.Lerp(color, new Color(0.27f, 0.4f, 0.3f), 0.32f);
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.SetColor(BaseColorId, color);
                    block.SetColor(ColorId, color);
                    renderer.SetPropertyBlock(block);
                }
            }
        }

        private void CaptureRiderHome()
        {
            if (_riderHomeCaptured || _player == null) return;
            _riderVisualRoot = _player.Find("PlayerVisualRoot");
            if (_riderVisualRoot == null) return;
            _riderBody = _riderVisualRoot.Find("Body");
            _riderHead = _riderVisualRoot.Find("Head");
            _riderLeftArm = _riderVisualRoot.Find("Left arm");
            _riderRightArm = _riderVisualRoot.Find("Right arm");
            _riderLeftLeg = _riderVisualRoot.Find("Left leg");
            _riderRightLeg = _riderVisualRoot.Find("Right leg");
            var parts = RiderParts();
            _riderHomePositions = new Vector3[parts.Length];
            _riderHomeRotations = new Quaternion[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                _riderHomePositions[i] = parts[i].localPosition;
                _riderHomeRotations[i] = parts[i].localRotation;
            }
            _riderHomeCaptured = true;
        }

        private Transform[] RiderParts()
        {
            return new[] { _riderVisualRoot, _riderBody, _riderHead, _riderLeftArm, _riderRightArm, _riderLeftLeg, _riderRightLeg };
        }

        private void ApplyRiderPose()
        {
            var mounted = Flag(Obj(_me, "horse"), "mounted") && !_firstPerson;
            if (!_riderHomeCaptured) CaptureRiderHome();
            if (!_riderHomeCaptured || _riderBody == null || _riderHead == null ||
                _riderLeftArm == null || _riderRightArm == null || _riderLeftLeg == null || _riderRightLeg == null) return;
            var parts = RiderParts();
            if (!mounted)
            {
                for (var i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == null) continue;
                    parts[i].localPosition = _riderHomePositions[i];
                    parts[i].localRotation = _riderHomeRotations[i];
                }
                return;
            }

            // Presentation-only seated pose. The networked entity root and camera target stay
            // at the authoritative ground position; only the visible rig is reshaped.
            _riderVisualRoot.localPosition = new Vector3(0f, 0.34f, 0f);
            _riderBody.localPosition = new Vector3(0f, 1.02f, 0f);
            _riderBody.localRotation = Quaternion.Euler(8f, 0f, 0f);
            _riderHead.localPosition = new Vector3(0f, 1.68f, 0.03f);
            _riderHead.localRotation = Quaternion.Euler(-5f, 0f, 0f);
            _riderLeftArm.localPosition = new Vector3(-0.34f, 1.22f, 0.16f);
            _riderRightArm.localPosition = new Vector3(0.34f, 1.22f, 0.16f);
            _riderLeftArm.localRotation = Quaternion.Euler(58f, 0f, -7f);
            _riderRightArm.localRotation = Quaternion.Euler(58f, 0f, 7f);
            _riderLeftLeg.localPosition = new Vector3(-0.22f, 0.48f, 0.1f);
            _riderRightLeg.localPosition = new Vector3(0.22f, 0.48f, 0.1f);
            _riderLeftLeg.localRotation = Quaternion.Euler(82f, 0f, -12f);
            _riderRightLeg.localRotation = Quaternion.Euler(82f, 0f, 12f);
        }

        private void LateUpdate()
        {
            if (_active && _me != null) ApplyRiderPose();
        }

        private void UpdateHandLight()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (_handLight == null)
            {
                var go = new GameObject("Hand light");
                go.transform.SetParent(cam.transform, false);
                go.transform.localPosition = new Vector3(0.3f, -0.1f, 0.4f);
                _handLight = go.AddComponent<Light>();
                _handLight.type = LightType.Point;
                _handLight.range = 22f;
                _handLight.intensity = 0f;
            }
            var combat = Obj(_me, "combat");
            var weapon = Str(combat, "weapon");
            var weaveLight = Num(combat, "light") > 0f;
            var lit = weaveLight || weapon == "torch" || weapon == "lantern";
            var night = _hour >= 20 || _hour < 6 ? 1f : _hour >= 18 || _hour < 8 ? 0.55f : 0.15f;
            _handLight.color = weaveLight ? new Color(0.75f, 0.85f, 1f) : new Color(1f, 0.76f, 0.48f);
            _handLight.intensity = Mathf.Lerp(_handLight.intensity, lit ? 2.2f * night : 0f, 1f - Mathf.Exp(-6f * Time.deltaTime));
        }

        // ------------------------------------------------------------------ weave (mouse-traced pattern)

        private void OpenWeave()
        {
            _weaveOpen = true;
            _pattern.Clear();
            _weaveLastNode = -1;
            if (_rig != null) _rig.SetMouseGestureCapture(true); // mouse draws the pattern, not the view
        }

        private void CloseWeave(bool cast)
        {
            if (!_weaveOpen) return;
            _weaveOpen = false;
            if (_rig != null) _rig.SetMouseGestureCapture(false);
            if (!cast || _pattern.Count == 0) return;
            CoreFoeView foe;
            string part;
            float accuracy, distance;
            var target = AimFoe(out foe, out part, out accuracy, out distance) ? foe.FoeId : string.Empty;
            Ask("Плетение", "weave", "nodes", _pattern.ToArray(), "target", target);
        }

        private static Vector2 NodeCenter(int index)
        {
            var angle = -Mathf.PI / 2f + index * Mathf.PI / 3f;
            return new Vector2(Screen.width * 0.5f + Mathf.Cos(angle) * 150f, Screen.height * 0.5f + Mathf.Sin(angle) * 150f);
        }

        private int MaxPattern()
        {
            var max = (int)Num(Obj(_me, "weave"), "max_len");
            return max > 0 ? max : 12;
        }

        private void UpdateWeave()
        {
            for (var i = 0; i < Nodes.Length; i++)
                if (_input != null && _input.WeaveNodePressed(i) && _pattern.Count < MaxPattern()) _pattern.Add(Nodes[i]);
            if (_input != null && _input.WeaveRemovePressed && _pattern.Count > 0) _pattern.RemoveAt(_pattern.Count - 1);
            if (_input != null && _input.SecondaryPressed) { _pattern.Clear(); _weaveLastNode = -1; }

            var pointer = _input != null ? _input.PointerPosition : Vector2.zero;
            var mouse = new Vector2(pointer.x, Screen.height - pointer.y);
            var over = -1;
            for (var i = 0; i < Nodes.Length; i++)
                if (Vector2.Distance(mouse, NodeCenter(i)) < 36f) over = i;
            if (over >= 0 && over != _weaveLastNode)
            {
                if (_pattern.Count < MaxPattern()) _pattern.Add(Nodes[over]);
                _weaveLastNode = over;
            }
            else if (over < 0 && _weaveLastNode >= 0 && Vector2.Distance(mouse, NodeCenter(_weaveLastNode)) > 52f)
            {
                _weaveLastNode = -1; // left the node: the same node may be woven again
            }
        }

        private string ReadPattern(out int complexity, out int cost, out bool closed)
        {
            int knot = 0, thread = 0, loop = 0, fork = 0, barb = 0, seal = 0;
            foreach (var node in _pattern)
            {
                switch (node)
                {
                    case "knot": knot++; break;
                    case "thread": thread++; break;
                    case "loop": loop++; break;
                    case "fork": fork++; break;
                    case "barb": barb++; break;
                    case "seal": seal++; break;
                }
            }
            closed = _pattern.Count > 0 && _pattern[0] == "knot" && _pattern[_pattern.Count - 1] == "seal" && seal == 1;
            complexity = _pattern.Count + fork + (closed ? 0 : 3);
            cost = 4 * _pattern.Count + 4 * knot;
            if (barb > 0)
                return "ранит" + (thread > 0 ? " издали" : " вблизи") + (fork > 0 ? ", целей: " + (1 + fork) : "") + (loop > 0 ? ", потом жжёт" : "");
            if (loop > 0 && thread > 0) return "заживляет раны";
            if (loop > 0) return fork > 0 ? "щит на отряд" : "щит";
            if (thread > 0) return "отбрасывает и оглушает";
            return "свет над ладонью";
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            var delta = b - a;
            if (delta.sqrMagnitude < 0.01f) return;
            var matrix = GUI.matrix;
            var previous = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, delta.magnitude, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        private void DrawWeave()
        {
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var pointer = _input != null ? _input.PointerPosition : Vector2.zero;
            var mouse = new Vector2(pointer.x, Screen.height - pointer.y);
            var line = new Color(0.62f, 0.82f, 1f, 0.95f);
            for (var i = 1; i < _pattern.Count; i++)
                DrawLine(NodeCenter(Array.IndexOf(Nodes, _pattern[i - 1])), NodeCenter(Array.IndexOf(Nodes, _pattern[i])), line, 3f);
            if (_pattern.Count > 0)
                DrawLine(NodeCenter(Array.IndexOf(Nodes, _pattern[_pattern.Count - 1])), mouse, new Color(0.62f, 0.82f, 1f, 0.5f), 2f);

            for (var i = 0; i < Nodes.Length; i++)
            {
                var c = NodeCenter(i);
                GUI.Box(new Rect(c.x - 44f, c.y - 30f, 88f, 60f), NodeGlyph[i] + "\n" + (i + 1) + " " + NodeRu[i]);
            }

            var weave = Obj(_me, "weave");
            var capacity = Num(weave, "capacity");
            var glyphs = string.Empty;
            foreach (var node in _pattern) glyphs += NodeGlyph[Array.IndexOf(Nodes, node)] + " ";
            var text = _pattern.Count == 0 ? "Веди мышью по узлам (или 1–6). Начни узлом, закончи печатью." : glyphs;
            if (_pattern.Count > 0)
            {
                int complexity, cost;
                bool closed;
                var what = ReadPattern(out complexity, out cost, out closed);
                var over = complexity - capacity;
                var risk = over <= -1f ? "удержишь" : over <= 1f ? "на пределе" : "надорвёшься";
                text += "\nСделает: " + what + " · сложность " + complexity + " из " + capacity.ToString("0.#", CultureInfo.InvariantCulture)
                    + " (" + risk + ") · маны " + cost + (closed ? "" : " · узор НЕ замкнут");
            }
            var known = Arr(weave, "known");
            if (known != null)
            {
                text += "\nЗнаю: ";
                foreach (var schema in known)
                {
                    text += Str(schema, "name") + " (";
                    var nodes = Arr(schema, "nodes");
                    if (nodes != null)
                        foreach (var node in nodes)
                        {
                            var index = Array.IndexOf(Nodes, node.ToString());
                            if (index >= 0) text += NodeGlyph[index] + " ";
                        }
                    text += ")   ";
                }
            }
            text += "\nОтпусти R — сплести · ПКМ — сброс · Esc — бросить";
            GUI.Box(new Rect(center.x - 380f, center.y + 205f, 760f, 104f), text);
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            if (!_active || _me == null) return;

            if (_hurtFlash > 0f)
            {
                var previous = GUI.color;
                GUI.color = new Color(0.6f, 0.05f, 0.03f, Mathf.Clamp01(_hurtFlash) * 0.45f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            var journey = Obj(_me, "journey");
            if (journey != null)
            {
                DrawRoad(journey);
                DrawFeed();
                return;
            }

            DrawFoeLabels();
            DrawStatus();
            DrawFeed();
            DrawQuestTracker();
            if (_hitMarker > 0f)
                GUI.Label(new Rect(Screen.width * 0.5f - 10f, Screen.height * 0.5f - 34f, 40f, 24f), "x");
            DrawFloaters();
            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime < _toastUntil)
                GUI.Box(new Rect(Screen.width * 0.5f - 150f, Screen.height * 0.62f, 300f, 28f), _toast);
            if (_weaveOpen) DrawWeave();
            if (_menuOpen) DrawMenu();
        }

        private void DrawFoeLabels()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var pair in _foes)
            {
                var rec = pair.Value;
                if (rec.Root == null || rec.View.Dead) continue;
                if (!rec.Aware && rec.Hp >= rec.Max) continue;
                var screen = cam.WorldToScreenPoint(rec.Root.transform.position + Vector3.up * (rec.View.Height + 0.35f));
                if (screen.z <= 0f || screen.z > 60f) continue;
                var rect = new Rect(screen.x - 50f, Screen.height - screen.y - 22f, 100f, 20f);
                GUI.Label(rect, rec.View.DisplayName);
                var previous = GUI.color;
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(rect.x, rect.y + 20f, 100f, 6f), Texture2D.whiteTexture);
                GUI.color = new Color(0.79f, 0.42f, 0.35f);
                GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 21f, 98f * Mathf.Clamp01(rec.Hp / rec.Max), 4f), Texture2D.whiteTexture);
                GUI.color = previous;
            }
        }

        private void DrawFloaters()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var floater in _floaters)
            {
                var age = Time.unscaledTime - floater.At;
                var screen = cam.WorldToScreenPoint(floater.World + Vector3.up * (age * 0.9f));
                if (screen.z <= 0f) continue;
                var previous = GUI.color;
                var color = floater.Hurt ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.88f, 0.5f);
                color.a = Mathf.Clamp01(1.2f - age);
                GUI.color = color;
                GUI.Label(new Rect(screen.x - 20f, Screen.height - screen.y - 10f, 60f, 22f), floater.Text);
                GUI.color = previous;
            }
        }

        private static void Bar(Rect rect, float value01, Color color)
        {
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * Mathf.Clamp01(value01), rect.height - 2f), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawStatus()
        {
            var combat = Obj(_me, "combat");
            var x = Screen.width * 0.5f - 170f;
            var y = Screen.height - 74f;
            Bar(new Rect(x, y, 340f, 10f), Num(_me, "health") / 100f, new Color(0.55f, 0.66f, 0.42f));
            Bar(new Rect(x, y + 13f, 340f, 8f), Num(Obj(_me, "pos"), "stam"), new Color(0.85f, 0.76f, 0.42f));
            Bar(new Rect(x, y + 24f, 340f, 8f), Num(_me, "mana") / 100f, new Color(0.5f, 0.65f, 0.88f));
            var weapon = Str(combat, "weapon");
            var stance = Str(combat, "stance");
            var line = (string.IsNullOrEmpty(weapon) ? "кулаки" : Name(weapon))
                + (weapon == "bow" ? " · стрел " + (int)Num(combat, "arrows") : "")
                + " · стойка: " + (stance == "aggressive" ? "яростная" : stance == "defensive" ? "осторожная" : "ровная")
                + (Flag(combat, "guard") ? " · БЛОК" : "") + (Flag(combat, "sneak") ? " · крадёшься" : "")
                + (Flag(combat, "feint") ? " · финт готов" : "")
                + (Num(combat, "shield") > 0f ? " · щит " + (int)Num(combat, "shield") : "");
            GUI.Label(new Rect(x, y + 34f, 520f, 22f), line);
            if (_showHints)
                GUI.Label(new Rect(x - 120f, y + 52f, 900f, 22f),
                    "ЛКМ удар (зажать — тяжёлый/натяг) · ПКМ блок · Q уворот · F укол · R плетение · Tab дела · Z песок · X толчок · V финт · B приманка · C красться · T стойка · H скрыть подсказки");
        }

        private void DrawFeed()
        {
            var y = Screen.height - 120f - _feed.Count * 20f;
            foreach (var line in _feed)
            {
                var previous = GUI.color;
                GUI.color = line.Kind == "hurt" || line.Kind == "backfire" || line.Kind == "ko"
                    ? new Color(0.95f, 0.63f, 0.56f)
                    : line.Kind == "kill" || line.Kind == "learn" || line.Kind == "quest" ? new Color(0.91f, 0.74f, 0.4f) : Color.white;
                GUI.Label(new Rect(Screen.width * 0.5f - 300f, y, 600f, 22f), line.Text);
                GUI.color = previous;
                y += 20f;
            }
        }

        private void DrawQuestTracker()
        {
            var active = Arr(Obj(_me, "quests"), "active");
            if (active == null || active.Count == 0 || _menuOpen) return;
            var y = 14f;
            foreach (var quest in active)
            {
                var kind = Str(quest, "kind");
                var progress = kind == "deliver" || kind == "cull"
                    ? " (" + Mathf.Min((int)Num(quest, "have"), (int)Num(quest, "qty")) + "/" + (int)Num(quest, "qty") + ")"
                    : string.Empty;
                GUI.Label(new Rect(Screen.width - 430f, y, 420f, 40f), (Flag(quest, "ready") ? "[готово] " : "- ") + Str(quest, "text") + progress);
                y += 40f;
            }
        }

        private static string OptionRu(string option)
        {
            switch (option)
            {
                case "fight": return "Драться";
                case "pay": return "Откупиться";
                case "flee": return "Бежать";
                case "scare": return "Отпугнуть";
                case "talk": return "Поговорить";
                case "trade": return "Купить хлеба";
                case "pass": return "Пройти мимо";
                case "rest": return "Отдохнуть у огня";
                case "gather": return "Набрать трав";
                default: return option;
            }
        }

        private void DrawRoad(JObject journey)
        {
            var width = Mathf.Min(680f, Screen.width - 40f);
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f - 170f, width, 340f);
            GUI.Box(rect, "Дорога в " + Str(journey, "to_name"));
            GUILayout.BeginArea(new Rect(rect.x + 16f, rect.y + 30f, rect.width - 32f, rect.height - 40f));
            Bar(GUILayoutUtility.GetRect(rect.width - 32f, 12f), Num(journey, "progress"), new Color(0.79f, 0.64f, 0.29f));
            GUILayout.Space(8f);
            GUILayout.Label(Str(journey, "scene"));
            var note = Str(journey, "note");
            if (!string.IsNullOrEmpty(note)) GUILayout.Label(note);
            var roadEvent = Obj(journey, "event");
            if (roadEvent != null)
            {
                GUILayout.Space(8f);
                GUILayout.Label(Str(roadEvent, "text"));
                if (Flag(journey, "follow"))
                {
                    GUILayout.Label("Решает вожак отряда…");
                }
                else
                {
                    var options = Arr(roadEvent, "options");
                    for (var i = 0; options != null && i < options.Count; i++)
                    {
                        var option = options[i].ToString();
                        if (GUILayout.Button("[" + (i + 1) + "] " + OptionRu(option), GUILayout.Height(30f)))
                            Ask("Выбор пути", "journey", "action", "choose", "choice", option);
                    }
                }
            }
            else
            {
                GUILayout.Label("Ещё примерно " + (int)Num(journey, "eta") + " с пути.");
            }
            GUILayout.EndArea();
        }

        private bool HasFeature(string feature)
        {
            var site = Obj(_place, "site");
            var features = Arr(site, "features");
            if (features == null) return true; // older frame: preserve legacy affordances
            foreach (var value in features)
                if (string.Equals(value.ToString(), feature, StringComparison.Ordinal)) return true;
            return false;
        }

        private bool Near(string poi, float radius)
        {
            Vector2 anchor;
            if (!Poi.TryGetValue(poi, out anchor)) return false;
            var pos = Obj(_me, "pos");
            return pos != null && Vector2.Distance(new Vector2(Num(pos, "x"), Num(pos, "y")), anchor) <= radius;
        }

        private static float Count(JObject bag, string key)
        {
            return bag == null ? 0f : Num(bag, key);
        }

        private void DrawMenu()
        {
            var width = Mathf.Min(900f, Screen.width - 40f);
            var height = Mathf.Min(680f, Screen.height - 40f);
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(rect, "Дела  [Tab — закрыть]");
            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 28f, rect.width - 28f, rect.height - 38f));
            _menuScroll = GUILayout.BeginScrollView(_menuScroll);

            var inv = Obj(_me, "inv");
            var items = Obj(inv, "items");
            var worn = Obj(inv, "worn");
            var hand = Str(inv, "hand");
            var stash = Obj(_me, "stash");
            var coins = Num(_me, "coins");

            GUILayout.Label("ЗДЕСЬ");
            var site = Obj(_place, "site");
            if (site != null)
            {
                var have = string.Empty;
                var features = Arr(site, "features");
                if (features != null)
                    foreach (var feature in features)
                        have += (feature.ToString() == "river" ? "река" : feature.ToString() == "woods" ? "лес" : feature.ToString() == "mine" ? "рудник" : "поля") + "  ";
                GUILayout.Label("Село славится: " + Str(site, "specialty") + " · держава " + Str(site, "state") + " · рядом есть: " + have);
            }
            if (_place != null && Flag(_place, "tavern") && Near("tavern", 12f))
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = !Flag(_me, "mug") && Num(_place, "beer") >= 1f;
                if (GUILayout.Button("Заказать пива (" + Num(_place, "beer_price").ToString("0.#", CultureInfo.InvariantCulture) + " монеты)")) Ask("Пиво", "buy", "item", "beer");
                GUI.enabled = Flag(_me, "mug");
                if (GUILayout.Button("Выпить")) Ask("Выпить", "consume", "item", "beer");
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (HasFeature("mine") && Near("mine", 10f) && GUILayout.Button("Добыть руду")) Ask("Руда", "act", "action", "mine");
            if (HasFeature("garden") && Near("garden", 10f) && GUILayout.Button("Собрать травы")) Ask("Травы", "act", "action", "gather");
            if (Flag(Obj(_me, "weave"), "can_learn") && GUILayout.Button("Созерцать ману (учиться плетению)")) Ask("Созерцание", "act", "action", "meditate");
            if (HasFeature("woods") && Near("woods", 10f) && GUILayout.Button("Срубить дерево")) Ask("Рубка", "act", "action", "chop");
            if (HasFeature("river") && Near("river", 10f) && GUILayout.Button("Закинуть удочку")) Ask("Рыбалка", "act", "action", "fish");
            foreach (var pair in TakeAt)
                if (Near(pair.Value, 10f) && Count(items, pair.Key) < 1f && GUILayout.Button("Взять: " + Name(pair.Key)))
                    Ask("Взять", "take", "item", pair.Key);

            var recipes = Arr(_me, "recipes");
            if (recipes != null)
            {
                foreach (var recipe in recipes)
                {
                    if (!Flag(recipe, "here")) continue;
                    var needs = string.Empty;
                    var needObj = Obj(recipe, "needs");
                    if (needObj != null)
                        foreach (var need in needObj.Properties())
                            needs += Name(need.Name) + " ×" + need.Value + "  ";
                    GUI.enabled = Flag(recipe, "can");
                    if (GUILayout.Button(Str(recipe, "name") + "  —  нужно: " + needs)) Ask(Str(recipe, "name"), "craft", "item", Str(recipe, "id"));
                    GUI.enabled = true;
                }
            }

            var market = Arr(_place, "market");
            if (market != null && (Near("market", 12f) || Near("granary", 12f)))
            {
                GUILayout.Label("ПРИЛАВОК  (монет: " + coins.ToString("0.#", CultureInfo.InvariantCulture) + ")");
                var shown = 0;
                foreach (var row in market)
                {
                    if (shown++ >= 14) break;
                    var good = Str(row, "good");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Name(good) + " · склад " + (int)Num(row, "stock") + " · у тебя " + (int)Count(stash, good), GUILayout.Width(360f));
                    GUI.enabled = Num(row, "stock") >= 1f;
                    if (GUILayout.Button("купить " + Num(row, "price").ToString("0.#", CultureInfo.InvariantCulture))) Ask("Покупка", "trade", "good", good, "side", "buy", "qty", 1);
                    GUI.enabled = Count(stash, good) >= 1f;
                    if (GUILayout.Button("продать " + Num(row, "sell").ToString("0.#", CultureInfo.InvariantCulture))) Ask("Продажа", "trade", "good", good, "side", "sell", "qty", 1);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("СНАРЯЖЕНИЕ И ПОКЛАЖА");
            GUILayout.Label("В РУКАХ: " + (string.IsNullOrEmpty(hand) ? "пусто" : Name(hand)));
            var wornLine = string.Empty;
            if (worn != null)
                foreach (var slot in worn.Properties())
                    if (!string.IsNullOrEmpty(slot.Value.ToString()))
                        wornLine += slot.Name + ": " + Name(slot.Value.ToString()) + "   ";
            GUILayout.Label("НАДЕТО: " + (string.IsNullOrEmpty(wornLine) ? "ничего" : wornLine));

            var carriedLine = string.Empty;
            if (items != null)
                foreach (var item in items.Properties())
                    if (Num(items, item.Name) > 0f)
                        carriedLine += Name(item.Name) + " ×" + (int)Num(items, item.Name) + "   ";
            GUILayout.Label("ПРИ СЕБЕ: " + (string.IsNullOrEmpty(carriedLine) ? "пусто" : carriedLine));

            var bagLine = string.Empty;
            if (stash != null)
                foreach (var good in stash.Properties())
                    if (Num(stash, good.Name) >= 1f)
                        bagLine += Name(good.Name) + " ×" + (int)Num(stash, good.Name) + "   ";
            var bagCapacity = Obj(_me, "bag");
            GUILayout.Label("СУМА: " + (string.IsNullOrEmpty(bagLine) ? "пусто" : bagLine)
                + (bagCapacity != null ? "   ·   " + (int)Num(bagCapacity, "load") + "/" + (int)Num(bagCapacity, "cap") : ""));

            var saddleContents = Obj(Obj(_me, "horse"), "bag");
            var saddleLine = string.Empty;
            if (saddleContents != null)
                foreach (var good in saddleContents.Properties())
                    if (Num(saddleContents, good.Name) >= 1f)
                        saddleLine += Name(good.Name) + " ×" + (int)Num(saddleContents, good.Name) + "   ";
            GUILayout.Label("СЕДЕЛЬНАЯ СУМКА: " + (string.IsNullOrEmpty(saddleLine) ? "пусто" : saddleLine));
            GUILayout.BeginHorizontal();
            foreach (var item in new[] { "axe", "sword", "knife", "bow", "rod", "torch", "lantern" })
                if (Count(items, item) > 0f && hand != item && GUILayout.Button("в руку: " + Name(item))) Ask("Экипировка", "equip", "item", item);
            if (!string.IsNullOrEmpty(hand) && GUILayout.Button("убрать из руки")) Ask("Экипировка", "equip", "item", string.Empty);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var item in new[] { "cloak", "boots", "hat", "jerkin", "shield" })
            {
                var already = false;
                if (worn != null)
                    foreach (var slot in worn.Properties())
                        if (slot.Value.ToString() == item) already = true;
                if (Count(items, item) > 0f && !already && GUILayout.Button("надеть: " + Name(item))) Ask("Одежда", "wear", "item", item);
            }
            foreach (var item in new[] { "stew", "roast", "potion", "bandage" })
                if (Count(items, item) > 0f && GUILayout.Button("употребить: " + Name(item))) Ask("Употребить", "use", "item", item);
            foreach (var good in new[] { "bread", "fish", "meat", "cheese", "veg" })
                if (Count(stash, good) >= 1f && GUILayout.Button("съесть: " + Name(good))) Ask("Еда", "use", "item", good);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            var quests = Obj(_me, "quests");
            GUILayout.Label("ПРОСЬБЫ  (выполнено " + (int)Num(quests, "done") + ", слава здесь " + (int)Num(quests, "rep") + ")");
            var active = Arr(quests, "active");
            if (active != null)
                foreach (var quest in active)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((Flag(quest, "ready") ? "[готово] " : "... ") + Str(quest, "text"), GUILayout.Width(rect.width - 260f));
                    GUI.enabled = Flag(quest, "ready");
                    if (GUILayout.Button("сдать")) Ask("Просьба", "quest", "action", "turnin", "quest", Str(quest, "id"));
                    GUI.enabled = true;
                    if (GUILayout.Button("отказаться")) Ask("Просьба", "quest", "action", "drop", "quest", Str(quest, "id"));
                    GUILayout.EndHorizontal();
                }
            var offers = Arr(quests, "offers");
            var currentTalk = Obj(_me, "talk");
            var currentTalkWith = Str(currentTalk, "with");
            if (offers != null)
                foreach (var offer in offers)
                {
                    GUILayout.BeginHorizontal();
                    var via = Str(offer, "via");
                    var giver = Str(offer, "giver");
                    var isCurrentNpc = via == "npc" && !string.IsNullOrEmpty(currentTalkWith) && giver == currentTalkWith;
                    var who = via == "board" ? "доска у колодца"
                        : isCurrentNpc ? "собеседник " + Str(offer, "giver_name")
                        : Str(offer, "giver_name");
                    var pay = Num(offer, "coins") > 0f ? "  [" + (int)Num(offer, "coins") + " монет]" : string.Empty;
                    GUILayout.Label(who + ": " + Str(offer, "text") + pay, GUILayout.Width(rect.width - 210f));
                    if (via != "npc" || isCurrentNpc)
                    {
                        if (GUILayout.Button(isCurrentNpc ? "взяться у него" : "взяться"))
                            Ask("Просьба", "quest", "action", "accept", "quest", Str(offer, "id"));
                    }
                    else
                    {
                        GUILayout.Label("поговори с жителем");
                    }
                    GUILayout.EndHorizontal();
                }

            GUILayout.Space(8f);
            var hereId = Str(_place, "id");
            var hereOuter = false;
            var world = Arr(_me, "world");
            if (world != null)
                foreach (var place in world)
                    if (Str(place, "id") == hereId) hereOuter = Flag(place, "outer");
            GUILayout.Label("КАРТА КРАЯ (только то, где был и о чём слышал; путь идёт по трактам)");
            DrawRegion(hereId);

            var stats = Obj(_me, "stats");
            if (stats != null)
            {
                GUILayout.Space(8f);
                GUILayout.Label("КАКОВ ТЫ САМ (растёт от дела)");
                GUILayout.Label("сила " + Num(stats, "strength") + "   ловкость " + Num(stats, "agility") + "   выносливость " + Num(stats, "endurance")
                    + "   ум " + Num(stats, "intellect") + "   восприятие " + Num(stats, "perception") + "   воля " + Num(stats, "will"));
                GUILayout.Label(Flag(Obj(_me, "weave"), "gift")
                    ? "В тебе есть дар: ум удержит и сложный узор."
                    : "Дара нет: по силам лишь самые простые узоры. Учиться можно в местах силы и у магов.");
                if (Num(_me, "drunk") > 0f) GUILayout.Label("Хмель: " + (int)Num(_me, "drunk"));
                var bagInfo = Obj(_me, "bag");
                if (bagInfo != null)
                    GUILayout.Label("Сума: " + (int)Num(bagInfo, "load") + " из " + (int)Num(bagInfo, "cap")
                        + (Flag(bagInfo, "over") ? " — ПЕРЕГРУЗ: идёшь медленно" : ""));
            }

            GUILayout.Space(8f);
            GUILayout.Label("ЛОШАДЬ");
            var horse = Obj(_me, "horse");
            var stable = Obj(_place, "stable");
            if (horse == null)
            {
                if (stable != null && Near("granary", 10f))
                {
                    GUI.enabled = Num(stable, "horses") >= 1f && coins >= Num(stable, "price");
                    if (GUILayout.Button("Купить лошадь у конюха (" + (int)Num(stable, "price") + " монет, в конюшне " + (int)Num(stable, "horses") + ")"))
                        Ask("Лошадь", "horse", "action", "buy");
                    GUI.enabled = true;
                }
                else GUILayout.Label("Лошади нет. Их продаёт конюх у амбара.");
            }
            else
            {
                GUILayout.Label(Str(horse, "name") + " · силы " + (int)Num(horse, "stam") + " · седельная сумка "
                    + (int)Num(horse, "bag_load") + "/" + (int)Num(horse, "bag_cap") + (Flag(horse, "here") ? "" : " · осталась в другом селе"));
                if (Flag(horse, "here"))
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(Flag(horse, "mounted") ? "Слезть" : "В седло"))
                        Ask("Лошадь", "horse", "action", Flag(horse, "mounted") ? "dismount" : "mount");
                    if (GUILayout.Button("Покормить")) Ask("Корм", "horse", "action", "feed");
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    if (stash != null)
                        foreach (var packed in stash.Properties())
                            if (Num(stash, packed.Name) >= 1f && GUILayout.Button("на лошадь: " + Name(packed.Name)))
                                Ask("Вьюк", "horse", "action", "stow", "good", packed.Name, "qty", 1);
                    var saddle = Obj(horse, "bag");
                    if (saddle != null)
                        foreach (var packed in saddle.Properties())
                            if (GUILayout.Button("с лошади: " + Name(packed.Name)))
                                Ask("Вьюк", "horse", "action", "unstow", "good", packed.Name, "qty", 1);
                    GUILayout.EndHorizontal();
                }
            }

            var party = Obj(_me, "party");
            var members = Arr(party, "members");
            var partyInvite = Obj(party, "invite_from");
            GUILayout.Space(8f);
            GUILayout.Label("ОТРЯД");
            if (partyInvite != null && GUILayout.Button(Str(partyInvite, "who") + " зовёт в отряд — вступить")) Ask("Отряд", "party", "action", "accept");
            if (members != null && members.Count > 0)
            {
                var list = string.Empty;
                foreach (var member in members)
                    list += (Flag(member, "leader") ? "* " : "") + Str(member, "name") + " (" + (int)Num(member, "health") + ")   ";
                GUILayout.Label(list);
                if (GUILayout.Button("Выйти из отряда")) Ask("Отряд", "party", "action", "leave");
            }
            var neighbors = Arr(_place, "neighbors");
            if (neighbors != null)
                foreach (var neighbor in neighbors)
                {
                    if (!Flag(neighbor, "player")) continue;
                    var label = Flag(neighbor, "known") ? Str(neighbor, "name") : Str(neighbor, "desc");
                    if (GUILayout.Button("Позвать в отряд: " + label)) Ask("Отряд", "party", "action", "invite", "target", Str(neighbor, "id"));
                }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // Regional map as a list: server-known nodes only (me.region). Unknown road ends carry no name.
        private void DrawRegion(string hereId)
        {
            var region = Obj(_me, "region");
            var nodes = Arr(region, "nodes");
            var roads = Arr(region, "roads");
            if (nodes == null) return;
            DrawRegionMap(nodes, roads);
            foreach (var node in nodes)
            {
                var id = Str(node, "id");
                var state = Str(node, "state");
                if (state == "here") continue;
                var title = state == "unknown" ? "куда-то ведёт тракт" : Str(node, "name");
                var mark = state == "visited" ? "бывал" : state == "known" ? "слышал" : "неведомо";
                var length = string.Empty;
                if (roads != null)
                    foreach (var road in roads)
                        if ((Str(road, "a") == id && Str(road, "b") == hereId) || (Str(road, "b") == id && Str(road, "a") == hereId))
                            length = " · " + (int)Num(road, "len") + " вёрст";
                if (GUILayout.Button(title + "  (" + mark + length + ")"))
                {
                    Ask("Дорога", "journey", "target", id);
                    _menuOpen = false;
                }
            }
        }

        // The regional map is a projection of me.region only. Coordinates and edges are
        // server supplied; absent settlements stay in the fog and unknown nodes are nameless.
        private void DrawRegionMap(JArray nodes, JArray roads)
        {
            var rect = GUILayoutUtility.GetRect(420f, 190f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            var points = new Dictionary<string, Vector2>();
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var node in nodes)
            {
                var id = Str(node, "id");
                var x = Num(node, "x");
                var y = Num(node, "y");
                points[id] = new Vector2(x, y);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            if (points.Count == 0) return;
            var inner = new Rect(rect.x + 18f, rect.y + 24f, rect.width - 36f, rect.height - 42f);
            const float pad = 18f;
            var spanX = Mathf.Max(1f, maxX - minX);
            var spanY = Mathf.Max(1f, maxY - minY);
            Vector2 Project(JToken node)
            {
                var x = Num(node, "x");
                var y = Num(node, "y");
                return new Vector2(inner.x + pad + (x - minX) / spanX * Mathf.Max(1f, inner.width - pad * 2f),
                    inner.yMax - pad - (y - minY) / spanY * Mathf.Max(1f, inner.height - pad * 2f));
            }

            if (roads != null)
            {
                foreach (var road in roads)
                {
                    Vector2 a, b;
                    if (!points.TryGetValue(Str(road, "a"), out a) || !points.TryGetValue(Str(road, "b"), out b)) continue;
                    var delta = b - a;
                    var length = delta.magnitude;
                    if (length < 1f) continue;
                    var previous = GUI.color;
                    GUI.color = new Color(0.58f, 0.48f, 0.32f, 0.9f);
                    var matrix = GUI.matrix;
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
                    GUI.DrawTexture(new Rect(a.x, a.y - 1.5f, length, 3f), Texture2D.whiteTexture);
                    GUI.matrix = matrix;
                    GUI.color = previous;
                }
            }

            foreach (var node in nodes)
            {
                var state = Str(node, "state");
                var point = Project(node);
                var previous = GUI.color;
                GUI.color = state == "here" ? new Color(0.95f, 0.78f, 0.35f)
                    : state == "visited" ? new Color(0.65f, 0.78f, 0.55f)
                    : state == "known" ? new Color(0.58f, 0.7f, 0.83f)
                    : new Color(0.55f, 0.53f, 0.48f);
                GUI.DrawTexture(new Rect(point.x - 5f, point.y - 5f, 10f, 10f), Texture2D.whiteTexture);
                GUI.color = previous;
                var label = state == "unknown" ? "?" : Str(node, "name");
                if (state == "here") label = "Ты";
                if (!string.IsNullOrEmpty(label))
                    GUI.Label(new Rect(point.x + 7f, point.y - 10f, 130f, 20f), label);
            }
        }

        private void DrawPlaces(JArray world, string hereId, bool hereOuter, bool sameSide)
        {
            if (world == null) return;
            var column = 0;
            var shown = 0;
            GUILayout.BeginHorizontal();
            foreach (var place in world)
            {
                var id = Str(place, "id");
                if (id == hereId || (Flag(place, "outer") == hereOuter) != sameSide) continue;
                if (shown++ >= 24) break;
                if (column == 4)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    column = 0;
                }
                column++;
                if (GUILayout.Button((Str(place, "tier") == "city" ? "город " : "") + Str(place, "name")))
                {
                    // Only op:"journey" (a road that takes time). op:"travel" is a thin-client/gateway intent
                    // and is deliberately not offered by the thick client (ADR 0003).
                    Ask("Дорога", "journey", "target", id);
                    _menuOpen = false;
                }
            }
            GUILayout.EndHorizontal();
        }
    }
}
