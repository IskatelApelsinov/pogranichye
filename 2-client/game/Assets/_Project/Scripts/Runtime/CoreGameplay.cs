using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Pogranichye.Client.Presentation;
using Pogranichye.Client.Networking;
using Pogranichye.Client.Networking.Protocol;
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

        private static Texture2D _uiPanelTexture;
        private static Texture2D _uiButtonTexture;
        private static Texture2D _uiButtonHoverTexture;
        private static Texture2D _uiButtonActiveTexture;

        private static Texture2D MakeUiTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        /// <summary>Shared parchment-and-iron IMGUI theme for every thick-client panel.</summary>
        public static void ApplyUiTheme()
        {
            if (Event.current == null || GUI.skin == null) return;
            if (_uiPanelTexture == null) _uiPanelTexture = MakeUiTexture(new Color(0.075f, 0.083f, 0.073f, 0.97f));
            if (_uiButtonTexture == null) _uiButtonTexture = MakeUiTexture(new Color(0.19f, 0.17f, 0.13f, 0.98f));
            if (_uiButtonHoverTexture == null) _uiButtonHoverTexture = MakeUiTexture(new Color(0.34f, 0.27f, 0.16f, 1f));
            if (_uiButtonActiveTexture == null) _uiButtonActiveTexture = MakeUiTexture(new Color(0.48f, 0.34f, 0.17f, 1f));

            var label = GUI.skin.label;
            label.fontSize = 15;
            label.wordWrap = true;
            label.normal.textColor = new Color(0.88f, 0.85f, 0.76f);
            label.margin = new RectOffset(4, 4, 3, 4);

            var box = GUI.skin.box;
            box.normal.background = _uiPanelTexture;
            box.normal.textColor = new Color(0.91f, 0.76f, 0.48f);
            box.fontSize = 15;
            box.fontStyle = FontStyle.Bold;
            box.alignment = TextAnchor.UpperLeft;
            box.padding = new RectOffset(12, 12, 10, 10);
            box.margin = new RectOffset(4, 4, 5, 5);

            var button = GUI.skin.button;
            button.normal.background = _uiButtonTexture;
            button.hover.background = _uiButtonHoverTexture;
            button.active.background = _uiButtonActiveTexture;
            button.focused.background = _uiButtonHoverTexture;
            button.normal.textColor = new Color(0.91f, 0.88f, 0.79f);
            button.hover.textColor = Color.white;
            button.active.textColor = Color.white;
            button.fontSize = 14;
            button.fontStyle = FontStyle.Bold;
            button.padding = new RectOffset(9, 9, 6, 6);
            button.margin = new RectOffset(3, 3, 3, 3);

            var field = GUI.skin.textField;
            field.normal.background = _uiButtonTexture;
            field.focused.background = _uiButtonHoverTexture;
            field.normal.textColor = Color.white;
            field.fontSize = 14;
            field.padding = new RectOffset(7, 7, 5, 5);
        }

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
            public Transform Telegraph;
        }

        private const float PendingTimeout = 6f;

        private sealed class Floater
        {
            public Vector3 World;
            public string Text;
            public float At;
            public bool Hurt;
            public Transform OwnerRoot;
        }

        private sealed class FeedLine
        {
            public string Kind;
            public string Text;
            public float At;
        }

        // Mirror of Layout v2 POI anchors (metres, server x/y). Only used to decide which actions the
        // "Дела" window offers; the server validates the real position for every intent.
        // Legacy starter-layout anchors are used only when no server POI map is available.
        private static readonly Dictionary<string, PoiCoordinate> Poi = new Dictionary<string, PoiCoordinate>
        {
            { "tavern", new PoiCoordinate(-33d, 12d) }, { "smithy", new PoiCoordinate(29d, 14d) },
            { "granary", new PoiCoordinate(-2d, 28d) }, { "market", new PoiCoordinate(1d, 4d) },
            { "well", new PoiCoordinate(0d, -4d) }, { "hearth", new PoiCoordinate(33d, -32d) },
            { "garden", new PoiCoordinate(70d, -20d) }, { "apiary", new PoiCoordinate(73.6d, -20.9d) },
            { "river", new PoiCoordinate(0d, -74d) }, { "woods", new PoiCoordinate(-95d, 10d) },
            { "mine", new PoiCoordinate(90d, 40d) }
        };

        private WorldLayoutFrame _layout;
        private readonly Dictionary<string, PoiCoordinate> _layoutPois = new Dictionary<string, PoiCoordinate>();
        private bool _hasLayoutPois;
        private bool _allowLegacyPoiFallback;

        private static readonly Dictionary<string, string> TakeAt = new Dictionary<string, string>
        {
            { "axe", "woods" }, { "rod", "river" }, { "sword", "smithy" }, { "bow", "smithy" },
            { "torch", "market" }, { "lantern", "market" }, { "cloak", "granary" }, { "boots", "granary" },
            { "hat", "granary" }
        };

        // Keep presentation prices aligned with the server's itemPrice table.
        // The server remains authoritative and revalidates every purchase.
        private static readonly Dictionary<string, int> TakePrice = new Dictionary<string, int>
        {
            { "axe", 8 }, { "rod", 6 }, { "sword", 18 }, { "bow", 15 },
            { "torch", 2 }, { "lantern", 6 }, { "cloak", 8 }, { "boots", 6 }, { "hat", 3 }
        };

        private static readonly Dictionary<string, string> Ru = new Dictionary<string, string>
        {
            { "axe", "топор" }, { "sword", "меч" }, { "bow", "лук" }, { "rod", "удочка" }, { "torch", "факел" },
            { "lantern", "фонарь" }, { "knife", "нож" }, { "cloak", "плащ" }, { "boots", "сапоги" },
            { "hat", "шапка" }, { "jerkin", "кожаная куртка" }, { "shield", "щит" }, { "purse", "кожаный кошель" }, { "arrow", "стрелы" },
            { "bandage", "повязка" }, { "potion", "снадобье" }, { "stew", "уха" }, { "roast", "жаркое" },
            { "bread", "хлеб" }, { "fish", "рыба" }, { "wood", "дерево" }, { "meat", "мясо" }, { "hide", "шкура" },
            { "wheat", "зерно" }, { "veg", "овощи" }, { "ore", "руда" }, { "metal", "железо" },
            { "leather", "кожа" }, { "herb", "травы" }, { "flour", "мука" }, { "game", "дичь" },
            { "cheese", "сыр" }, { "egg", "яйца" }, { "milk", "молоко" }, { "wool", "шерсть" },
            { "cloth", "сукно" }, { "tools", "инструмент" }, { "hay", "сено" }, { "feather", "перо" },
            { "flax", "лён" }, { "linen", "холст" }, { "remedy", "лекарство" }, { "honey", "мёд" },
            { "mead", "медовуха" },
            { "woodcutter", "лесоруб" }, { "miner", "рудокоп" }, { "smith", "кузнец" },
            { "blade", "владение клинком" }, { "brawl", "рукопашный бой" }, { "archery", "стрельба из лука" },
            { "hunter", "охота" }, { "fisher", "рыбалка" }, { "herbalist", "травничество" },
            { "cunning", "хитрость" }, { "mage", "магия" }, { "cook", "кулинария" },
            { "crafter", "ремесло" }, { "farmer", "земледелие" }, { "herder", "скотоводство" },
            { "weaver", "ткачество" }, { "builder", "строительство" }
        };

        // v1.22 server catalogue: the six foundational nodes plus heat/chill/veil/eye.
        // Mouse weaving exposes every node; legacy 1–6 keyboard shortcuts remain stable.
        private static readonly string[] Nodes = { "knot", "thread", "loop", "fork", "barb", "seal", "heat", "chill", "veil", "eye" };
        private static readonly string[] NodeRu = { "узел", "нить", "петля", "развилка", "шип", "печать", "жар", "стужа", "пелена", "око" };
        // Short Cyrillic marks instead of symbol glyphs: the default IMGUI font is not guaranteed to have them.
        private static readonly string[] NodeGlyph = { "Уз", "Нт", "Пт", "Рз", "Шп", "Пч", "Жр", "Ст", "Пл", "Ок" };

        private Action<Dictionary<string, object>> _send;
        private bool _active;
        private bool _firstPerson;
        private bool _hudCollapsed;
        private Transform _player;
        private PresentationCameraRig _rig;
        private ClientInput _input;
        private JObject _me;
        private JObject _place;
        private JObject _catalog;
        private bool _catalogExpanded;
        private string _inventoryCategory = "Все";
        private string _inventorySearch = string.Empty;
        private bool _inventoryStorageExpanded;
        private bool _inventoryDetailsExpanded;
        private GameplayStatusView _status;
        private ActionResultFrame _result;
        private CombatFrame _combat;
        private JourneyFrame _journey;
        private QuestsFrame _quests;
        private StatsFrame _stats;
        private CarryBagFrame _bag;
        private HorseFrame _horse;
        private RegionFrame _region;
        private TavernCounterFrame _tavernCounter;
        private CardGameFrame _cardGame;
        private int _hour = 12;

        private readonly Dictionary<string, FoeRec> _foes = new Dictionary<string, FoeRec>();
        private readonly Dictionary<string, GameObject> _exitViews = new Dictionary<string, GameObject>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly List<string> _stale = new List<string>();
        private Transform _foeRoot;
        private MaterialPropertyBlock _block;

        private readonly List<FeedLine> _feed = new List<FeedLine>();
        private readonly List<Floater> _floaters = new List<Floater>();
        private readonly RaycastHit[] _visibilityHits = new RaycastHit[24];
        private readonly RaycastHit[] _aimHits = new RaycastHit[48];
        private bool _aimHitBufferSaturated;
        private string _lockedFoeId;
        private RequestGate _gate;
        private int _chopSwings;
        private int _fishPhase; // 0 idle, 1 waiting for a bite, 2 bite window
        private float _fishAt;
        private string _toast;
        private float _toastUntil;
        private bool _showHints = false;
        private long _feedSeq;
        private float _hurtFlash;
        private float _hitMarker;

        private bool _menuOpen;
        private bool _externalCharacterMenuOpen;
        // The outer HUD owns additional pointer surfaces (character journal, context actions, minigames, chat).
        // The controller reports those so this component never re-locks the cursor over them.
        private bool _externalUiCapture;
        private bool _externalModalMenuOpen;
        private GUIStyle _questTitleStyle;
        private GUIStyle _questBodyStyle;
        private GUIStyle _hintTextStyle;
        private GUIStyle _toastStyle;
        private Vector2 _menuScroll;
        private Vector2 _marketRowsScroll;
        private float _regionMapZoom = 1f;
        private bool _weaveOpen;
        private readonly List<string> _pattern = new List<string>();
        private int _weaveLastNode = -1;
        private bool _holding;
        private float _holdStart;
        private bool _guardSent;
        private Light _handLight;

        /// <summary>True while the character menu, weave wheel, or external modal menu owns gameplay input.</summary>
        public bool CapturesInput { get { return GameplayInputCapturePolicy.Captures(_externalCharacterMenuOpen || _menuOpen, _weaveOpen, _externalModalMenuOpen, _externalUiCapture); } }

        public void SetExternalCharacterMenuOpen(bool open)
        {
            if (_externalCharacterMenuOpen == open) return;
            _externalCharacterMenuOpen = open;
            if (!open) return;
            _menuOpen = false;
            _holding = false;
            _holdStart = 0f;
            CloseWeave(false);
            ReleaseGuard();
        }

        public void SetExternalUiCapture(bool captured)
        {
            _externalUiCapture = captured;
        }

        public void SetExternalModalMenuOpen(bool open)
        {
            _externalModalMenuOpen = open;
            if (!open) return;
            _externalCharacterMenuOpen = false;
            _menuOpen = false;
            _holding = false;
            _holdStart = 0f;
            CloseWeave(false);
            ReleaseGuard();
        }

        /// <summary>Raised on the Unity main thread when a queued result-bearing request resolves.</summary>
        public event Action<string, RequestGate.Outcome, string> RequestResolved;

        /// <summary>Read-only view of the shared result-bearing queue for the gameplay HUD.</summary>
        public bool HasPendingRequests => _gate != null && (_gate.Busy || _gate.Waiting > 0);
        public string PendingRequestLabel => _gate != null ? _gate.InFlightLabel : null;
        public int PendingRequestCount => _gate == null ? 0 : (_gate.Busy ? 1 : 0) + _gate.Waiting;

        public void Bind(Action<Dictionary<string, object>> sender)
        {
            _send = sender;
        }

        public void BindInput(ClientInput input)
        {
            _input = input;
        }

        public void SetCatalog(JObject catalog)
        {
            // The catalogue is static reference data. Keep the last validated snapshot across
            // ordinary world frames; it is never used to predict an action or mutate inventory.
            _catalog = catalog;
        }

        private static void DrawAttribute(StatsFrame stats, string key, string label)
        {
            if (stats == null) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(stats.Get(key).ToString("0.#", CultureInfo.InvariantCulture), GUILayout.Width(48f));
            GUILayout.EndHorizontal();
        }

        private static void DrawCondition(JObject me, string key, string label, Color color)
        {
            if (me == null || me[key] == null) return;
            var value = Mathf.Clamp(Num(me, key), 0f, 100f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(112f));
            var barRect = GUILayoutUtility.GetRect(100f, 8f, GUILayout.ExpandWidth(true));
            Bar(barRect, value / 100f, color);
            GUILayout.Label(value.ToString("0", CultureInfo.InvariantCulture), GUILayout.Width(32f));
            GUILayout.EndHorizontal();
        }

        private JObject CatalogItem(string id)
        {
            var items = Arr(_catalog, "items");
            if (items == null || string.IsNullOrEmpty(id)) return null;
            foreach (var item in items)
                if (string.Equals(Str(item, "item"), id, StringComparison.Ordinal)) return item as JObject;
            return null;
        }

        private string CatalogName(string id)
        {
            var names = Obj(_catalog, "names");
            var catalogName = Str(names, id);
            return string.IsNullOrEmpty(catalogName) ? Name(id) : catalogName;
        }

        private string CatalogItemName(JObject definition, string fallback)
        {
            var name = Str(definition, "name");
            return string.IsNullOrEmpty(name) ? CatalogName(fallback) : name;
        }

        private void DrawCatalogReference()
        {
            if (_catalog == null) return;
            var items = Arr(_catalog, "items");
            var recipes = Arr(_catalog, "recipes");
            var nodes = Arr(_catalog, "weave_nodes");
            var schemas = Arr(_catalog, "weave_schemas");
            var label = "СПРАВОЧНИК МИРА · " + (items?.Count ?? 0) + " предметов · " +
                (recipes?.Count ?? 0) + " рецептов · " + (nodes?.Count ?? 0) + " узлов плетения";
            _catalogExpanded = GUILayout.Toggle(_catalogExpanded, label, GUI.skin.button);
            if (!_catalogExpanded) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("ПРЕДМЕТЫ И СНАРЯЖЕНИЕ");
            if (items != null)
            {
                foreach (var item in items)
                {
                    var name = Str(item, "name");
                    var id = Str(item, "item");
                    if (string.IsNullOrEmpty(name)) name = Name(id);
                    var details = new List<string>();
                    var slot = Str(item, "slot");
                    if (!string.IsNullOrEmpty(slot)) details.Add("слот: " + slot);
                    if (Flag(item, "hand")) details.Add("в руку");
                    if (item["price"] != null) details.Add("цена: " + Num(item, "price").ToString("0.#", CultureInfo.InvariantCulture));
                    foreach (var effect in new[] { "armor", "carry", "pace", "rest" })
                        if (item[effect] != null && Num(item, effect) != 0)
                            details.Add(effect + " +" + Num(item, effect).ToString("0.#", CultureInfo.InvariantCulture));
                    var weapon = Obj(item, "weapon");
                    if (weapon != null)
                        details.Add("урон " + Num(weapon, "dmg").ToString("0.#", CultureInfo.InvariantCulture) +
                            " · дальность " + Num(weapon, "reach").ToString("0.#", CultureInfo.InvariantCulture) + " м");
                    var use = Obj(item, "use");
                    if (use != null)
                    {
                        if (Num(use, "health") > 0) details.Add("здоровье +" + Num(use, "health").ToString("0.#", CultureInfo.InvariantCulture));
                        if (Num(use, "energy") > 0) details.Add("силы +" + Num(use, "energy").ToString("0.#", CultureInfo.InvariantCulture));
                        if (Num(use, "hunger") > 0) details.Add("сытость +" + Num(use, "hunger").ToString("0.#", CultureInfo.InvariantCulture));
                    }
                    GUILayout.Label(name + (details.Count == 0 ? "" : " · " + string.Join(" · ", details.ToArray())));
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label("РЕМЕСЛО · РЕЦЕПТЫ");
            if (recipes != null)
            {
                foreach (var recipe in recipes)
                {
                    var needs = Obj(recipe, "needs");
                    var parts = new List<string>();
                    if (needs != null)
                        foreach (var need in needs.Properties())
                            parts.Add(CatalogName(need.Name) + " ×" + need.Value);
                    GUILayout.Label(Str(recipe, "name") + " · " + Str(recipe, "at") +
                        (parts.Count == 0 ? "" : " · нужно: " + string.Join(", ", parts.ToArray())) +
                        " · результат: " + CatalogName(Str(recipe, "gives")) + " ×" + Num(recipe, "qty").ToString("0", CultureInfo.InvariantCulture));
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label("УЗЛЫ И СХЕМЫ ПЛЕТЕНИЙ");
            if (nodes != null)
                foreach (var node in nodes)
                    GUILayout.Label(Str(node, "name") + " · " + Str(node, "hint"));
            if (schemas != null)
                foreach (var schema in schemas)
                {
                    var sequence = Arr(schema, "nodes");
                    var ids = new List<string>();
                    if (sequence != null)
                        foreach (var node in sequence) ids.Add(node.ToString());
                    GUILayout.Label(Str(schema, "name") + ": " + string.Join(" → ", ids.ToArray()));
                }
            GUILayout.EndVertical();
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

            // Header: currency and carrying capacity are authoritative frame values.
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.BeginVertical();
            GUILayout.Label("СНАРЯЖЕНИЕ И ВЕЩИ");
            GUILayout.Label("Монеты · " + Num(_me, "coins").ToString("0.#", CultureInfo.InvariantCulture));
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (_bag != null)
            {
                GUILayout.BeginVertical();
                GUILayout.Label("Нагрузка");
                var load = (float)_bag.Load;
                var cap = Mathf.Max(1f, (float)_bag.Capacity);
                var old = GUI.color;
                GUI.color = _bag.Overloaded ? new Color(0.82f, 0.39f, 0.28f) : new Color(0.62f, 0.69f, 0.48f);
                var bar = GUILayoutUtility.GetRect(130f, 9f, GUILayout.Width(130f));
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width, bar.height), Texture2D.blackTexture);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(load / cap), bar.height), Texture2D.whiteTexture);
                GUI.color = old;
                GUILayout.Label(load.ToString("0.#") + " / " + cap.ToString("0.#"));
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("ЭКИПИРОВКА · ОБРАЗ ПЕРСОНАЖА");
            GUILayout.Label("Это те же серверные слоты, по которым обновляется 3D-модель персонажа в мире.");
            GUILayout.BeginHorizontal();
            DrawEquipmentSlot("Правая рука", hand, "hand", items);
            DrawEquipmentSlot("Голова", WornItem(worn, "head"), "head", items);
            DrawEquipmentSlot("Одежда", WornItem(worn, "body"), "body", items);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawEquipmentSlot("Броня", WornItem(worn, "armor"), "armor", items);
            DrawEquipmentSlot("Ноги", WornItem(worn, "feet"), "feet", items);
            DrawEquipmentSlot("Левая рука", WornItem(worn, "offhand"), "offhand", items);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawEquipmentSlot("Пояс", WornItem(worn, "belt"), "belt", items);
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("СУМКА", GUILayout.Width(62f));
            _inventorySearch = GUILayout.TextField(_inventorySearch ?? string.Empty, GUILayout.MinWidth(100f), GUILayout.MaxWidth(220f));
            GUILayout.FlexibleSpace();
            string[] categories = { "Все", "Оружие", "Одежда", "Расходники", "Материалы" };
            var selected = Array.IndexOf(categories, _inventoryCategory);
            if (selected < 0) selected = 0;
            selected = GUILayout.Toolbar(selected, categories, GUILayout.MinWidth(280f), GUILayout.Height(28f));
            _inventoryCategory = categories[selected];
            GUILayout.EndHorizontal();

            var visible = new List<JProperty>();
            if (items != null)
            {
                foreach (var property in items.Properties())
                {
                    if (Num(items, property.Name) < 1) continue;
                    var name = CatalogItemName(CatalogItem(property.Name), property.Name);
                    if (!string.IsNullOrWhiteSpace(_inventorySearch) &&
                        name.IndexOf(_inventorySearch.Trim(), StringComparison.OrdinalIgnoreCase) < 0 &&
                        property.Name.IndexOf(_inventorySearch.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var definition = CatalogItem(property.Name);
                    var slot = InventorySlot(definition, property.Name);
                    var isWeapon = IsInventoryHandItem(definition, property.Name);
                    var isClothing = !string.IsNullOrEmpty(slot) && !isWeapon;
                    var isUsable = IsCatalogUsable(property.Name);
                    var category = isWeapon ? "Оружие" : isClothing ? "Одежда" : isUsable ? "Расходники" : "Материалы";
                    if (_inventoryCategory != "Все" && _inventoryCategory != category) continue;
                    visible.Add(property);
                }
            }
            visible.Sort((x, y) => string.Compare(
                CatalogItemName(CatalogItem(x.Name), x.Name),
                CatalogItemName(CatalogItem(y.Name), y.Name), StringComparison.Ordinal));

            if (visible.Count == 0)
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.MinHeight(90f));
                GUILayout.FlexibleSpace();
                GUILayout.Label(string.IsNullOrWhiteSpace(_inventorySearch)
                    ? "В этой сумке пока ничего нет."
                    : "По этому запросу ничего не найдено.", GUI.skin.label);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
            }
            else
            {
                var columns = Screen.width < 900 ? 2 : 3;
                for (var i = 0; i < visible.Count; i += columns)
                {
                    GUILayout.BeginHorizontal();
                    for (var column = 0; column < columns && i + column < visible.Count; column++)
                    {
                        var item = visible[i + column];
                        var quantity = (int)Num(items, item.Name);
                        var definition = CatalogItem(item.Name);
                        var slot = InventorySlot(definition, item.Name);
                        var isWeapon = IsInventoryHandItem(definition, item.Name);
                        var isUsable = IsCatalogUsable(item.Name);
                        var isClothing = !string.IsNullOrEmpty(slot) && !isWeapon;
                        var category = isWeapon ? "Оружие" : isClothing ? "Одежда" : isUsable ? "Расходники" : "Материалы";
                        var equipped = hand == item.Name || IsWorn(worn, item.Name);
                        GUILayout.BeginVertical(GUI.skin.box, GUILayout.MinWidth(100f), GUILayout.MinHeight(126f), GUILayout.ExpandWidth(true));
                        GUILayout.BeginHorizontal();
                        DrawInventoryItemIcon(item.Name, category);
                        GUILayout.BeginVertical();
                        GUILayout.Label(CatalogItemName(definition, item.Name));
                        GUILayout.Label(category + " · × " + quantity);
                        GUILayout.EndVertical();
                        GUILayout.EndHorizontal();
                        var itemSummary = InventoryItemSummary(definition);
                        if (!string.IsNullOrEmpty(itemSummary))
                            GUILayout.Label(itemSummary, GUILayout.MinHeight(26f));
                        GUILayout.FlexibleSpace();
                        if (isWeapon)
                        {
                            if (GUILayout.Button(hand == item.Name ? "В руке" : "Взять"))
                                Ask("Экипировка", "equip", "item", item.Name);
                        }
                        else if (!string.IsNullOrEmpty(slot))
                        {
                            if (GUILayout.Button(equipped ? "Снять" : "Надеть"))
                                Ask("Одежда", equipped ? "unwear" : "wear", "item", item.Name);
                        }
                        else if (isUsable)
                        {
                            if (GUILayout.Button("Использовать"))
                                Ask("Использовать", "use", "item", item.Name);
                        }
                        else GUILayout.Label("Предмет");
                        GUILayout.EndVertical();
                    }
                    for (var empty = visible.Count - (i + columns); empty > 0 && empty < columns; empty--)
                        GUILayout.Space(1f);
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(6f);
            _inventoryDetailsExpanded = GUILayout.Toggle(_inventoryDetailsExpanded,
                _inventoryDetailsExpanded ? "▼ Состояние, характеристики и знания" : "▶ Состояние, характеристики и знания", GUI.skin.button);
            if (_inventoryDetailsExpanded)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("СОСТОЯНИЕ");
                DrawCondition(_me, "health", "Здоровье", new Color(0.55f, 0.66f, 0.42f));
                DrawCondition(_me, "hunger", "Голод", new Color(0.82f, 0.58f, 0.32f));
                DrawCondition(_me, "energy", "Силы", new Color(0.45f, 0.68f, 0.82f));
                DrawCondition(_me, "morale", "Настрой", new Color(0.68f, 0.55f, 0.78f));
                if (_stats != null)
                {
                    GUILayout.Space(4f);
                    GUILayout.Label("ХАРАКТЕРИСТИКИ");
                    DrawAttribute(_stats, "strength", "Сила");
                    DrawAttribute(_stats, "agility", "Ловкость");
                    DrawAttribute(_stats, "endurance", "Выносливость");
                    DrawAttribute(_stats, "intellect", "Ум");
                    DrawAttribute(_stats, "perception", "Восприятие");
                    DrawAttribute(_stats, "will", "Воля");
                }
                var skills = Obj(_me, "skills");
                if (skills != null && skills.HasValues)
                {
                    GUILayout.Space(4f);
                    GUILayout.Label("НАВЫКИ");
                    var rows = new List<JProperty>(skills.Properties());
                    rows.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.Ordinal));
                    foreach (var skill in rows)
                    {
                        double value;
                        if (skill.Value == null || !double.TryParse(skill.Value.ToString(),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                            double.IsNaN(value) || double.IsInfinity(value)) continue;
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(Name(skill.Name));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(value.ToString("0.#", CultureInfo.InvariantCulture), GUILayout.Width(52f));
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.Space(4f);
                DrawCatalogReference();
                GUILayout.EndVertical();
            }

            GUILayout.Space(6f);
            _inventoryStorageExpanded = GUILayout.Toggle(_inventoryStorageExpanded,
                _inventoryStorageExpanded ? "▼ Хранилища и поклажа" : "▶ Хранилища и поклажа", GUI.skin.button);
            if (_inventoryStorageExpanded)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("ЗАКРОМА ПОСЕЛЕНИЯ");
                var hasStash = false;
                if (stash != null)
                {
                    var goods = new List<string>();
                    foreach (var good in stash.Properties())
                        if (Num(stash, good.Name) >= 1f) goods.Add(good.Name);
                    goods.Sort(StringComparer.Ordinal);
                    foreach (var good in goods)
                    {
                        hasStash = true;
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(CatalogName(good) + " ×" + (int)Num(stash, good));
                        if (IsCatalogUsable(good) && GUILayout.Button("Использовать", GUILayout.Width(112f)))
                            Ask("Еда", "use", "item", good);
                        if (_horse != null && _horse.Here && GUILayout.Button("В седло", GUILayout.Width(88f)))
                            Ask("Вьюк", "horse", "action", "stow", "good", good, "qty", 1);
                        GUILayout.EndHorizontal();
                    }
                }
                if (!hasStash) GUILayout.Label("Закрома пусты или недоступны.");
                if (_horse != null)
                {
                    GUILayout.Space(5f);
                    GUILayout.Label("ЛОШАДЬ · " + _horse.Name + (_horse.Here ? "" : " · в другом поселении"));
                    GUILayout.Label("Седло: " + (int)_horse.BagLoad + " / " + (int)_horse.BagCapacity);
                    if (_horse.Bag != null)
                        foreach (var packed in _horse.Bag)
                            if (packed.Value > 0)
                            {
                                GUILayout.BeginHorizontal();
                                GUILayout.Label(CatalogName(packed.Key) + " ×" + (int)packed.Value);
                                if (_horse.Here && GUILayout.Button("Забрать", GUILayout.Width(112f)))
                                    Ask("Вьюк", "horse", "action", "unstow", "good", packed.Key, "qty", 1);
                                GUILayout.EndHorizontal();
                            }
                }
                else GUILayout.Label("Лошади пока нет.");
                GUILayout.EndVertical();
            }

            GUILayout.Space(5f);
            GUILayout.Label("Действия отправляются серверу. Слоты, характеристики и внешний вид персонажа обновляются только по подтверждённому состоянию мира.");
        }

        public void DrawQuestJournal()
        {
            if (!_active)
            {
                GUILayout.Label("Задания появятся после подключения к миру.");
                return;
            }

            var quests = _quests;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("ПРОСЬБЫ И ПОРУЧЕНИЯ");
            GUILayout.Label("Выполнено: " + (quests != null ? quests.Done : 0) +
                "   ·   Репутация в поселениях: " + (quests != null ? quests.Reputation : 0));
            var active = quests != null ? quests.Active : null;
            if (active == null || active.Count == 0)
            {
                GUILayout.Label("Сейчас нет принятых поручений.");
            }
            else
            {
                foreach (var quest in active)
                {
                    if (quest == null) continue;
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(quest.Text ?? "Поручение");
                    if (!string.IsNullOrWhiteSpace(quest.GiverName))
                        GUILayout.Label("Кто попросил: " + quest.GiverName);
                    if (!string.IsNullOrWhiteSpace(quest.SettlementName))
                        GUILayout.Label("Место: " + quest.SettlementName);
                    var have = Mathf.Max(0, quest.Have ?? 0);
                    var quantity = Mathf.Max(1, quest.Quantity);
                    GUILayout.Label(have + " / " + quantity);
                    var progress = GUILayoutUtility.GetRect(1f, 8f, GUILayout.ExpandWidth(true));
                    var oldColor = GUI.color;
                    GUI.color = new Color(0.13f, 0.14f, 0.12f, 1f);
                    GUI.DrawTexture(progress, Texture2D.whiteTexture);
                    GUI.color = new Color(0.65f, 0.53f, 0.34f, 1f);
                    GUI.DrawTexture(new Rect(progress.x, progress.y,
                        progress.width * Mathf.Clamp01(have / (float)quantity), progress.height), Texture2D.whiteTexture);
                    GUI.color = oldColor;
                    GUILayout.Label(quest.Ready == true ? "Готово к сдаче" : "Выполняется");
                    GUILayout.BeginHorizontal();
                    var previousEnabled = GUI.enabled;
                    GUI.enabled = previousEnabled && quest.Ready == true;
                    if (GUILayout.Button("Сдать"))
                        Ask("Поручение", "quest", "action", "turnin", "quest", quest.Id);
                    GUI.enabled = previousEnabled;
                    if (GUILayout.Button("Отказаться"))
                        Ask("Поручение", "quest", "action", "drop", "quest", quest.Id);
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }
            }

            GUILayout.Space(5f);
            GUILayout.Label("ДОСТУПНЫЕ ПРОСЬБЫ");
            var offers = quests != null ? quests.Offers : null;
            if (offers == null || offers.Count == 0)
            {
                GUILayout.Label("Новых просьб пока нет. Поговорите с жителями или загляните к доске объявлений.");
            }
            else
            {
                var talk = Obj(_me, "talk");
                var currentTalkWith = Str(talk, "with");
                foreach (var offer in offers)
                {
                    if (offer == null) continue;
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(offer.Text ?? "Просьба");
                    var via = offer.Via ?? string.Empty;
                    var giver = offer.Giver ?? string.Empty;
                    var isCurrentNpc = via == "npc" && !string.IsNullOrEmpty(currentTalkWith) && giver == currentTalkWith;
                    var who = via == "board" ? "Доска объявлений" :
                        !string.IsNullOrWhiteSpace(offer.GiverName) ? offer.GiverName : "Житель";
                    GUILayout.Label(who + (offer.Coins > 0d ? " · награда " + offer.Coins.ToString("0.##", CultureInfo.InvariantCulture) + " монет" : string.Empty));
                    if (via != "npc" || isCurrentNpc)
                    {
                        if (GUILayout.Button("Принять поручение"))
                            Ask("Поручение", "quest", "action", "accept", "quest", offer.Id);
                    }
                    else GUILayout.Label("Чтобы принять, сначала заговорите с этим жителем.");
                    GUILayout.EndVertical();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawEquipmentSlot(string label, string itemId, string slot, JObject items)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.MinWidth(70f), GUILayout.ExpandWidth(true), GUILayout.MinHeight(86f));
            GUILayout.Label(label);
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(itemId))
                DrawInventoryItemIcon(itemId, slot == "hand" ? "Оружие" : "Одежда");
            GUILayout.BeginVertical();
            GUILayout.Label(string.IsNullOrEmpty(itemId) ? "— пусто —" : CatalogName(itemId));
            var summary = InventoryItemSummary(CatalogItem(itemId));
            if (!string.IsNullOrEmpty(summary)) GUILayout.Label(summary);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            if (slot == "hand")
            {
                if (!string.IsNullOrEmpty(itemId) && GUILayout.Button("Убрать"))
                    Ask("Экипировка", "equip", "item", string.Empty);
            }
            else if (!string.IsNullOrEmpty(itemId) && GUILayout.Button("Снять"))
                Ask("Одежда", "unwear", "item", itemId);
            GUILayout.EndVertical();
        }

        private static void DrawInventoryItemIcon(string itemId, string category)
        {
            var previousBackground = GUI.backgroundColor;
            var previousContent = GUI.contentColor;
            switch (category)
            {
                case "Оружие": GUI.backgroundColor = new Color(0.48f, 0.34f, 0.20f, 1f); break;
                case "Одежда": GUI.backgroundColor = new Color(0.25f, 0.39f, 0.31f, 1f); break;
                case "Расходники": GUI.backgroundColor = new Color(0.48f, 0.43f, 0.23f, 1f); break;
                default: GUI.backgroundColor = new Color(0.32f, 0.35f, 0.34f, 1f); break;
            }
            GUI.contentColor = new Color(0.97f, 0.91f, 0.76f, 1f);
            GUILayout.Box(InventoryItemGlyph(itemId), GUILayout.Width(42f), GUILayout.Height(42f));
            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;
        }

        private static string InventoryItemGlyph(string itemId)
        {
            switch (itemId)
            {
                case "axe": return "Т";
                case "sword": return "М";
                case "knife": return "Н";
                case "bow": return "Л";
                case "rod": return "У";
                case "torch": return "Ф";
                case "lantern": return "Фн";
                case "shield": return "Щ";
                case "hat": return "Ш";
                case "cloak": return "П";
                case "boots": return "С";
                case "jerkin": return "К";
                case "bread": return "Х";
                case "fish": return "Р";
                case "stew": return "У";
                case "roast": return "Ж";
                case "bandage": return "Б";
                case "potion": return "З";
                case "wood": return "Д";
                case "ore": return "Рд";
                case "metal": return "Жл";
                case "leather": return "Кж";
                case "herb": return "Тр";
                case "wheat": return "Зр";
                case "meat": return "М";
                case "hide": return "Шк";
                default: return "·";
            }
        }

        private static string InventoryItemSummary(JObject definition)
        {
            if (definition == null) return string.Empty;
            var parts = new List<string>();
            if (definition["price"] != null)
                parts.Add("цена " + Num(definition, "price").ToString("0.#", CultureInfo.InvariantCulture));
            if (Num(definition, "armor") > 0)
                parts.Add("защита +" + Num(definition, "armor").ToString("0.#", CultureInfo.InvariantCulture));
            foreach (var stat in new[] { "carry", "pace", "rest" })
                if (Num(definition, stat) != 0)
                    parts.Add(stat + " +" + Num(definition, stat).ToString("0.#", CultureInfo.InvariantCulture));
            var weapon = Obj(definition, "weapon");
            if (weapon != null)
                parts.Add("урон " + Num(weapon, "dmg").ToString("0.#", CultureInfo.InvariantCulture) +
                    " · " + Num(weapon, "reach").ToString("0.#", CultureInfo.InvariantCulture) + " м");
            var use = Obj(definition, "use");
            if (use != null)
            {
                if (Num(use, "health") > 0) parts.Add("здоровье +" + Num(use, "health").ToString("0.#", CultureInfo.InvariantCulture));
                if (Num(use, "energy") > 0) parts.Add("энергия +" + Num(use, "energy").ToString("0.#", CultureInfo.InvariantCulture));
            }
            return string.Join(" · ", parts.ToArray());
        }

        private static string WornItem(JObject worn, string slot)
        {
            var value = Tok(worn, slot);
            return value == null ? null : value.ToString();
        }

        private static bool IsWorn(JObject worn, string itemId)
        {
            if (worn == null || string.IsNullOrEmpty(itemId)) return false;
            foreach (var property in worn.Properties())
                if (property.Value != null && property.Value.ToString() == itemId) return true;
            return false;
        }

        private static string InventorySlot(JToken definition, string itemId)
        {
            var slot = Str(definition, "slot");
            if (!string.IsNullOrEmpty(slot)) return slot;
            switch (itemId)
            {
                case "hat": return "head";
                case "cloak": return "body";
                case "boots": return "feet";
                case "jerkin": return "armor";
                case "shield": return "offhand";
                case "purse": return "belt";
                default: return string.Empty;
            }
        }

        private static bool IsInventoryHandItem(JToken definition, string itemId)
        {
            if (Flag(definition, "hand")) return true;
            switch (itemId)
            {
                case "axe": case "sword": case "knife": case "bow":
                case "rod": case "torch": case "lantern": return true;
                default: return false;
            }
        }

        private bool IsCatalogUsable(string itemId)
        {
            var usables = Arr(_catalog, "usables");
            if (usables != null)
            {
                foreach (var usable in usables)
                    if (usable != null && usable.ToString() == itemId) return true;
                return false;
            }

            // Compatibility with older v1 servers that do not yet expose /api/catalog.
            switch (itemId)
            {
                case "stew": case "roast": case "potion": case "bandage":
                case "bread": case "fish": case "meat": case "cheese":
                case "veg": case "game": case "egg": return true;
                default: return false;
            }
        }

        private static string WearSlotName(string slot)
        {
            switch (slot)
            {
                case "head": return "Голова";
                case "body": return "Тело";
                case "feet": return "Ноги";
                case "armor": return "Броня";
                case "offhand": return "Левая рука";
                case "belt": return "Пояс";
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

        /// <summary>Uses the current server Layout v2 POIs for local affordances; never changes server validation.</summary>
        public void SetLayout(WorldLayoutFrame layout, bool allowLegacyPoiFallback)
        {
            if (ReferenceEquals(_layout, layout) && _allowLegacyPoiFallback == allowLegacyPoiFallback) return;
            _layout = layout;
            _allowLegacyPoiFallback = allowLegacyPoiFallback;
            _layoutPois.Clear();
            _hasLayoutPois = layout != null && layout.Pois != null;
            if (!_hasLayoutPois) return;

            foreach (var pair in layout.Pois)
            {
                if (pair.Value == null) continue;
                _layoutPois[pair.Key] = new PoiCoordinate(pair.Value.X, pair.Value.Y);
            }
        }

        public void ApplyFrame(WorldFrame frame, JObject validatedDocument)
        {
            if (frame == null || validatedDocument == null) return;
            // The controller has already run the strict protocol parser. Consume the
            // exact token tree produced by that same parse so absent fields stay absent;
            // never reserialize DTO defaults or parse the wire JSON a second time here.
            var root = validatedDocument;
            _me = Obj(root, "me");
            _place = Obj(root, "place");
            _status = frame.Me == null
                ? null
                : GameplayStatusView.From(
                    frame.Me.Health, frame.Me.Energy, frame.Me.Mana, frame.Me.Drunk,
                    frame.Me.Position == null ? 0d : frame.Me.Position.Stamina);
            if (root["hour"] != null)
            {
                _hour = (int)Num(root, "hour");
                WorldEnvironmentBuilder.SetServerHour(_hour);
            }
            SyncFoes(frame.Place?.Foes);
            SyncExits(Arr(_place, "exits"));
            ApplySitePresentation();
            ReadFeed(frame.Me?.Feed);
            _combat = frame.Me?.Combat;
            _journey = frame.Me?.Journey;
            _quests = frame.Me?.Quests;
            _stats = frame.Me?.Stats;
            _bag = frame.Me?.Bag;
            _horse = frame.Me?.Horse;
            _region = frame.Me?.Region;
            _tavernCounter = frame.Place?.TavernCounter;
            _cardGame = frame.Me?.CardGame;
            _result = frame.Me?.Result;
            ReadResult(_result);
        }

        public void ApplyFastFrame(FastFramePatch patch)
        {
            if (_me == null || patch == null) return;
            _status = (_status ?? GameplayStatusView.Empty).Apply(patch.Status);
            if (patch.HasCombat) _combat = patch.Combat;
            var me = patch.Me;
            if (me != null)
            {
                foreach (var property in me.Properties())
                    _me[property.Name] = property.Value;
            }

            var place = patch.Place;
            if (place != null)
            {
                // Fast frames are partial patches. Preserve full-frame place fields
                // (layout, stock, tavern, anchor, etc.) that are intentionally omitted.
                if (_place == null) _place = new JObject();
                foreach (var property in place.Properties())
                    _place[property.Name] = property.Value;

                if (place.Property("exits") != null)
                    SyncExits(Arr(_place, "exits"));
                ApplySitePresentation();
            }
            if (patch.HasFoes) SyncFoes(patch.Foes);
            if (patch.HasFeed) ReadFeed(patch.Feed);
            if (patch.HasResult)
            {
                _result = patch.Result;
                ReadResult(_result);
            }
        }

        public void Clear()
        {
            ClearLockedTarget(false);
            _cancelPlayerWorkGesture();
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
            _status = null;
            _result = null;
            _cardGame = null;
            _combat = null;
            _journey = null;
            _quests = null;
            _stats = null;
            _bag = null;
            _horse = null;
            _region = null;
            _tavernCounter = null;
            _riderVisualRoot = null;
            _riderBody = _riderHead = _riderLeftArm = _riderRightArm = _riderLeftLeg = _riderRightLeg = null;
            _riderHomePositions = null;
            _riderHomeRotations = null;
            _riderHomeCaptured = false;
            _menuOpen = false;
            CloseWeave(false);
            _holding = false;
            _viewsPlayerGuard(false);
            _guardSent = false;
            if (_handLight != null) _handLight.intensity = 0f;
            if (_mount != null) _mount.SetActive(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            ClearLockedTarget(false);
            _viewsPlayerGuard(false);
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
        public bool IsRequestPending(string label, string op, params object[] pairs)
        {
            return _gate != null && _gate.ContainsDuplicate(label, Payload(op, pairs));
        }

        public bool QueueRequest(string label, string op, params object[] pairs)
        {
            if (_send == null) return false;
            if (_gate == null) _gate = new RequestGate(_send, PendingTimeout, 6);
            var payload = Payload(op, pairs);
            var reqid = Guid.NewGuid().ToString("N");
            payload["reqid"] = reqid;
            return _gate.Enqueue(label, reqid, payload, Time.unscaledTime);
        }

        private bool RequestButton(string buttonText, string requestLabel, string op, params object[] pairs)
        {
            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !IsRequestPending(requestLabel, op, pairs);
            var clicked = GUILayout.Button(buttonText);
            GUI.enabled = previousEnabled;
            if (clicked) Ask(requestLabel, op, pairs);
            return clicked;
        }

        private void Ask(string label, string op, params object[] pairs)
        {
            if (!QueueRequest(label, op, pairs))
                Toast("Не всё сразу: дождись ответа на прошлые действия", 1.6f);
        }

        // Action menu feedback is played only when the request actually enters the shared request gate.
        private void AskActivity(string label, string action)
        {
            if (!QueueRequest(label, "act", "action", action))
            {
                Toast("Не всё сразу: дождись ответа на прошлые действия", 1.6f);
                return;
            }

            if (action == "fish")
                _viewsPlayerGesture("fish_cast");
            else
                _viewsPlayerWorkGesture(action);
        }

        private void _viewsPlayerWorkGesture(string action)
        {
            var motion = _player != null ? _player.GetComponent<EntityMotion>() : null;
            if (motion != null) motion.SetWorkGesture(action);
        }

        private void _stopPlayerWorkGesture()
        {
            var motion = _player != null ? _player.GetComponent<EntityMotion>() : null;
            if (motion != null) motion.StopWorkGesture();
        }

        private void _cancelPlayerWorkGesture()
        {
            var motion = _player != null ? _player.GetComponent<EntityMotion>() : null;
            if (motion != null) motion.CancelWorkGesture();
        }

        private void ReadResult(ActionResultFrame result)
        {
            if (result == null || _gate == null) return;
            RequestGate.Item item;
            var reason = result.Reason ?? string.Empty;
            var outcome = _gate.OnResult(result.RequestId, result.Ok, Time.unscaledTime, out item);
            if (outcome == RequestGate.Outcome.Accepted || outcome == RequestGate.Outcome.Rejected)
            {
                _stopPlayerWorkGesture();
                RequestResolved?.Invoke(item.Label, outcome, reason);
            }
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
                _stopPlayerWorkGesture();
                RequestResolved?.Invoke(item.Label, RequestGate.Outcome.TimedOut, "сервер не ответил");
                Toast(item.Label + ": сервер не ответил", 2.2f);
            }
        }

        // ------------------------------------------------------------------ foes

        private void SyncFoes(IList<FoeFrame> foes)
        {
            _seen.Clear();
            if (foes != null)
            {
                foreach (var foe in foes)
                {
                    if (foe == null || string.IsNullOrEmpty(foe.Id)) continue;
                    var id = foe.Id;
                    _seen.Add(id);
                    FoeRec rec;
                    if (!_foes.TryGetValue(id, out rec))
                    {
                        rec = CreateFoe(id, foe.Kind, foe.Name);
                        rec.Root.transform.position = new Vector3((float)foe.X, 0f, (float)foe.Y);
                        _foes[id] = rec;
                    }
                    rec.Target = new Vector3((float)foe.X, 0f, (float)foe.Y);
                    rec.Yaw = EntityViewRegistry.FacingToUnityYaw((float)foe.Facing);
                    rec.Hp = (float)foe.Hp;
                    rec.Max = Mathf.Max(1f, (float)foe.Max);
                    rec.Aware = foe.Aware;
                    rec.State = foe.State ?? string.Empty;
                    if (rec.Telegraph != null) rec.Telegraph.gameObject.SetActive(rec.State == "windup");
                    if (rec.Gait != null)
                    {
                        rec.Gait.Dead = rec.State == "dead";
                        rec.Gait.Chasing = rec.State == "chase";
                        rec.Gait.Eating = rec.State == "eat";
                    }
                    rec.View.Dead = rec.State == "dead";
                    if (rec.View.Dead && string.Equals(_lockedFoeId, id, StringComparison.Ordinal))
                        ClearLockedTarget(false);
                }
            }

            _stale.Clear();
            foreach (var pair in _foes)
                if (!_seen.Contains(pair.Key)) _stale.Add(pair.Key);
            foreach (var id in _stale)
            {
                if (string.Equals(_lockedFoeId, id, StringComparison.Ordinal))
                    ClearLockedTarget(false);
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
            labelObject.AddComponent<BillboardLabel>();
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
            var telegraph = Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.025f, 0f),
                new Vector3(1.35f, 0.018f, 1.35f), new Color(0.82f, 0.12f, 0.055f));
            var telegraphCollider = telegraph.GetComponent<Collider>();
            if (telegraphCollider != null) Destroy(telegraphCollider);
            telegraph.gameObject.name = "Attack wind-up warning";
            telegraph.gameObject.SetActive(false);
            view.FoeId = id;
            view.Kind = kind;
            view.DisplayName = FoePresentationPolicy.ResolveDisplayName(kind, displayName);

            _surface = FoePresentationPolicy.IsHumanoid(kind) ? "cloth" : "fur";
            if (FoePresentationPolicy.IsHumanoid(kind))
            {
                view.Humanoid = true;
                view.Height = FoePresentationPolicy.BanditHeight;
                var cloth = new Color(0.35f, 0.12f, 0.10f);
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.4f, 0f), new Vector3(0.42f, 0.4f, 0.42f), new Color(0.2f, 0.17f, 0.14f));
                var torso = Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 1.15f, 0f), new Vector3(0.58f, 0.37f, 0.58f), cloth);
                var headRoot = new GameObject("Bandit head").transform;
                headRoot.SetParent(root.transform, false);
                headRoot.localPosition = new Vector3(0f, 1.7f, 0f);
                Part(headRoot, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.4f, 0.4f, 0.4f), new Color(0.3f, 0.2f, 0.16f));
                Part(headRoot, PrimitiveType.Sphere, new Vector3(0f, 0.06f, -0.04f), new Vector3(0.48f, 0.42f, 0.48f), new Color(0.16f, 0.12f, 0.1f));
                Part(headRoot, PrimitiveType.Sphere, new Vector3(-0.07f, 0.02f, 0.18f), new Vector3(0.05f, 0.05f, 0.05f), Color.white);
                Part(headRoot, PrimitiveType.Sphere, new Vector3(0.07f, 0.02f, 0.18f), new Vector3(0.05f, 0.05f, 0.05f), Color.white);
                var leftArm = new GameObject("Bandit left arm pivot").transform;
                leftArm.SetParent(root.transform, false);
                leftArm.localPosition = new Vector3(-0.36f, 1.44f, 0f);
                Part(leftArm, PrimitiveType.Cylinder, new Vector3(0f, -0.32f, 0f), new Vector3(0.14f, 0.32f, 0.14f), cloth);
                var rightArm = new GameObject("Bandit right arm pivot").transform;
                rightArm.SetParent(root.transform, false);
                rightArm.localPosition = new Vector3(0.36f, 1.44f, 0f);
                Part(rightArm, PrimitiveType.Cylinder, new Vector3(0f, -0.32f, 0f), new Vector3(0.14f, 0.32f, 0.14f), cloth);
                Part(rightArm, PrimitiveType.Cube, new Vector3(0f, -0.2f, 0.34f), new Vector3(0.07f, 0.07f, 0.9f), new Color(0.4f, 0.3f, 0.2f));
                var rightLeg = new GameObject("Bandit right leg pivot").transform;
                rightLeg.SetParent(root.transform, false);
                rightLeg.localPosition = new Vector3(0.17f, 0.66f, 0f);
                Part(rightLeg, PrimitiveType.Cylinder, new Vector3(0f, -0.32f, 0f), new Vector3(0.16f, 0.32f, 0.16f), new Color(0.2f, 0.17f, 0.14f));
                var leftLeg = new GameObject("Bandit left leg pivot").transform;
                leftLeg.SetParent(root.transform, false);
                leftLeg.localPosition = new Vector3(-0.17f, 0.66f, 0f);
                Part(leftLeg, PrimitiveType.Cylinder, new Vector3(0f, -0.32f, 0f), new Vector3(0.16f, 0.32f, 0.16f), new Color(0.2f, 0.17f, 0.14f));
                Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.86f, 0f), new Vector3(0.6f, 0.04f, 0.6f), new Color(0.12f, 0.09f, 0.07f));
                var gait = root.AddComponent<ProceduralGait>();
                gait.Humanoid = true;
                gait.LegLength = FoePresentationPolicy.BanditLimbLength;
                gait.StrideDegrees = FoePresentationPolicy.BanditStrideDegrees;
                gait.Legs.Add(leftArm);
                gait.Legs.Add(rightArm);
                gait.Legs.Add(rightLeg);
                gait.Legs.Add(leftLeg);
                gait.Body = torso;
                gait.Head = headRoot;
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

            return new FoeRec { Root = root, View = view, Gait = root.GetComponent<ProceduralGait>(), Telegraph = telegraph };
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

        private void ReadFeed(IList<FeedEntryFrame> feed)
        {
            if (feed == null) return;
            foreach (var item in feed)
            {
                if (item == null) continue;
                var seq = item.Sequence;
                if (seq <= _feedSeq) continue;
                _feedSeq = seq;
                var kind = item.Kind ?? string.Empty;
                var text = item.Text ?? string.Empty;
                var damage = (float)item.Damage;
                if (damage > 0f) text += " (" + Mathf.RoundToInt(damage) + ")";
                _feed.Add(new FeedLine { Kind = kind, Text = text, At = Time.unscaledTime });
                if (kind == "hurt" || kind == "backfire" || kind == "ko") _hurtFlash = kind == "ko" ? 1.6f : 0.35f;
                if (kind == "hit" || kind == "kill") _hitMarker = 0.18f;
                FoeRec struck;
                var foeId = item.Foe;
                if (damage > 0f && !string.IsNullOrEmpty(foeId) && _foes.TryGetValue(foeId, out struck) && struck.Root != null)
                {
                    var hurtsMe = kind == "hurt";
                    var anchor = hurtsMe && _player != null ? _player.position + Vector3.up * 1.4f
                        : struck.Root.transform.position + Vector3.up * (struck.View.Height + 0.2f);
                    _floaters.Add(new Floater { World = anchor, Text = (hurtsMe ? "-" : "") + Mathf.RoundToInt(damage), At = Time.unscaledTime, Hurt = hurtsMe,
                        OwnerRoot = hurtsMe ? _player : struck.Root.transform });
                    if (!hurtsMe)
                    {
                        struck.Punch = 0.2f;
                        if (struck.Gait != null) struck.Gait.HitReact(); // server-confirmed damage to this foe
                    }
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
                if (rec.Telegraph != null && rec.Telegraph.gameObject.activeSelf)
                {
                    var pulse = 1f + Mathf.Sin(Time.unscaledTime * 15f) * 0.12f;
                    rec.Telegraph.localScale = new Vector3(1.35f * pulse, 0.018f, 1.35f * pulse);
                }
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

            var journey = _journey;
            var talking = Obj(_me, "talk") != null;
            UpdateHandLight();
            UpdateMount();

            if (_externalModalMenuOpen)
            {
                _menuOpen = false;
                _holding = false;
                _holdStart = 0f;
                CloseWeave(false);
                ReleaseGuard();
                UpdateCursor(true);
                return;
            }

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
                var roadEvent = journey.Event;
                if (roadEvent != null && !journey.Follow)
                {
                    var options = roadEvent.Options;
                    for (var i = 0; options != null && i < options.Count && i < 9; i++)
                        if (_input != null && _input.JourneyChoicePressed(i))
                            Ask("Выбор пути", "journey", "action", "choose", "choice", options[i]);
                }
                UpdateCursor(true);
                return;
            }

            // Tab is owned by VerticalSliceController and opens the single character journal.
            // This component no longer creates a second, overlapping character menu.
            if (_input != null && _input.CancelPressed)
            {
                _menuOpen = false;
                CloseWeave(false);
            }

            if ((_menuOpen || _externalCharacterMenuOpen) && !talking)
                UpdateCardGameHotkeys();

            if (!_menuOpen && !_externalCharacterMenuOpen && !talking)
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

            UpdateCursor(_menuOpen || _externalCharacterMenuOpen || _weaveOpen || talking);
        }

        private void UpdateCursor(bool needsPointer)
        {
            // First person with the HUD folded away is the "immersive" state: the pointer is captured
            // and the crosshair aims. Any window that needs clicking releases it.
            var capture = _firstPerson && _hudCollapsed && !needsPointer && !_externalUiCapture;
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
                _viewsPlayerGuard(true);
                Send("guard", "on", true);
                _guardSent = true;
            }
            if (_input != null && _input.SecondaryReleased) ReleaseGuard();

            if (_input != null && _input.ToggleHintsPressed) _showHints = !_showHints;
            if (_input != null && _input.LockTargetPressed && !PointerOverHud())
                ToggleLockedTarget();
            if (_input != null && _input.DodgePressed)
            {
                _viewsPlayerGesture("dodge");
                Send("dodge");
            }
            if (_input != null && _input.QuickAttackPressed) Attack(0f, true);
            if (_input != null && _input.TrickSandPressed) Send("trick", "trick", "sand");
            if (_input != null && _input.TrickShovePressed) Send("trick", "trick", "shove");
            if (_input != null && _input.TrickFeintPressed) Send("trick", "trick", "feint");
            if (_input != null && _input.TrickBaitPressed) Send("trick", "trick", "bait");
            if (_input != null && _input.TrickSneakPressed) Send("trick", "trick", "sneak");
            if (_input != null && _input.CycleStancePressed)
            {
                var stance = _combat?.Stance ?? string.Empty;
                var next = stance == "balanced" ? "aggressive" : stance == "aggressive" ? "defensive" : "balanced";
                Send("stance", "stance", next);
            }
        }

        private void ReleaseGuard()
        {
            if (!_guardSent) return;
            _guardSent = false;
            _viewsPlayerGuard(false);
            Send("guard", "on", false);
        }

        private void _viewsPlayerGuard(bool guarding)
        {
            var motion = _player != null ? _player.GetComponent<EntityMotion>() : null;
            if (motion != null) motion.SetGuarding(guarding);
        }

        private void ToggleLockedTarget()
        {
            if (!string.IsNullOrEmpty(_lockedFoeId))
            {
                ClearLockedTarget(true);
                return;
            }

            var cam = Camera.main;
            var origin = _player != null ? _player.position : cam != null ? cam.transform.position : Vector3.zero;
            var forward = cam != null ? cam.transform.forward : _player != null ? _player.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            FoeRec best = null;
            var bestScore = float.MaxValue;
            foreach (var pair in _foes)
            {
                var rec = pair.Value;
                if (rec == null || rec.Root == null || rec.View == null || rec.View.Dead) continue;
                var toFoe = rec.Root.transform.position - origin;
                toFoe.y = 0f;
                var distance = toFoe.magnitude;
                if (distance < 0.1f || distance > 55f) continue;
                var direction = toFoe / distance;
                var dot = Vector3.Dot(forward, direction);
                if (dot < 0.35f) continue;
                var point = rec.Root.transform.position + Vector3.up * Mathf.Max(0.8f, rec.View.Height * 0.55f);
                if (cam != null && IsWorldPointOccluded(cam, point, rec.Root.transform)) continue;
                var score = distance * (1.25f - dot);
                if (score >= bestScore) continue;
                bestScore = score;
                best = rec;
            }

            if (best == null)
            {
                Toast("Нет противника перед тобой — наведи камеру на врага.", 1.8f);
                return;
            }

            _lockedFoeId = best.View.FoeId;
            if (_rig != null) _rig.SetLockOnTarget(best.Root.transform);
            Toast("Цель захвачена: " + best.View.DisplayName, 1.8f);
        }

        private void ClearLockedTarget(bool showToast)
        {
            var hadTarget = !string.IsNullOrEmpty(_lockedFoeId);
            _lockedFoeId = null;
            if (_rig != null) _rig.SetLockOnTarget(null);
            if (showToast && hadTarget) Toast("Захват цели снят.", 1.2f);
        }

        private bool AimFoe(out CoreFoeView view, out string part, out float accuracy, out float distance)
        {
            _aimHitBufferSaturated = false;
            view = null;
            part = "torso";
            accuracy = 0f;
            distance = 0f;
            var cam = Camera.main;
            if (cam == null) return false;
            if (!string.IsNullOrEmpty(_lockedFoeId))
            {
                FoeRec locked;
                if (_foes.TryGetValue(_lockedFoeId, out locked) && locked.Root != null &&
                    locked.View != null && !locked.View.Dead)
                {
                    var lockedPoint = locked.Root.transform.position + Vector3.up * Mathf.Max(0.8f, locked.View.Height * 0.55f);
                    if (IsWorldPointOccluded(cam, lockedPoint, locked.Root.transform))
                        return false;
                    var origin = _player != null ? _player.position : cam.transform.position;
                    var flat = locked.Root.transform.position - origin;
                    flat.y = 0f;
                    distance = flat.magnitude;
                    view = locked.View;
                    part = "torso";
                    accuracy = 0.82f;
                    return true;
                }
                ClearLockedTarget(false);
            }
            var ray = _firstPerson || Cursor.lockState == CursorLockMode.Locked
                ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : cam.ScreenPointToRay(_input != null ? _input.PointerPosition : Vector2.zero);
            var count = Physics.RaycastNonAlloc(ray, _aimHits, 140f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // A full NonAlloc buffer may contain an arbitrary subset, not necessarily the
            // nearest hits. Never select a foe when an omitted wall could be in front of it.
            if (count >= _aimHits.Length)
            {
                _aimHitBufferSaturated = true;
                return false;
            }
            if (count <= 0) return false;

            // RaycastNonAlloc does not guarantee hit order. Insertion sort the small fixed buffer
            // without allocating a temporary array or comparison delegate on every attack.
            for (var i = 1; i < count; i++)
            {
                var hit = _aimHits[i];
                var j = i - 1;
                while (j >= 0 && _aimHits[j].distance > hit.distance)
                {
                    _aimHits[j + 1] = _aimHits[j];
                    j--;
                }
                _aimHits[j + 1] = hit;
            }

            for (var i = 0; i < count; i++)
            {
                var hit = _aimHits[i];
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

        private void _viewsPlayerGesture(string action)
        {
            var motion = _player != null ? _player.GetComponent<EntityMotion>() : null;
            if (motion != null) motion.PlayActionGesture(action);
        }

        private void Attack(float held, bool thrust)
        {
            var combat = _combat;
            var ranged = combat != null && combat.Weapon == "bow";
            CoreFoeView foe;
            string part;
            float accuracy, distance;
            var aimed = AimFoe(out foe, out part, out accuracy, out distance);
            if (!aimed && !string.IsNullOrEmpty(_lockedFoeId))
            {
                Toast("Цель закрыта препятствием — найди угол для атаки.", 1.4f);
                return;
            }
            if (_aimHitBufferSaturated)
            {
                Toast("Слишком много объектов на линии прицела", 1.1f);
                return;
            }
            var weapon = combat?.Weapon ?? string.Empty;
            var lockedReach = ranged ? 50f : 3.0f;
            if (aimed && !string.IsNullOrEmpty(_lockedFoeId) && distance > lockedReach)
            {
                Toast(ranged ? "Враг слишком далеко для выстрела." : "Подойди ближе к захваченной цели.", 1.4f);
                return;
            }
            var pos = Obj(_me, "pos");
            if (!aimed && weapon == "axe" && pos != null && (Num(pos, "x") <= -70f || Near("woods", 8f)))
            {
                // Each local swing gets immediate tool feedback; the server still validates the completed action.
                _viewsPlayerGesture("chop");
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
                    _viewsPlayerGesture("fish_cast");
                    _fishPhase = 1;
                    _fishAt = Time.unscaledTime + UnityEngine.Random.Range(1.8f, 5f);
                    Toast("Заброс… жди поклёвки", 1.6f);
                }
                else if (_fishPhase == 2)
                {
                    _viewsPlayerGesture("fish_reel");
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
            _viewsPlayerGesture(ranged ? "bow_shot" : thrust ? "thrust" : held > 0.55f ? "heavy_attack" : "attack");
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
            // Keep a local snapshot for this presentation update; the server-owned horse view may be absent.
            var horse = _horse;
            var riding = horse != null && horse.Mounted && _player != null;
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
            var stamina = Mathf.Clamp((float)horse.Stamina, 0f, 100f);
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
            var mounted = _horse != null && _horse.Mounted && !_firstPerson;
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
            var combat = _combat;
            var weapon = combat?.Weapon ?? string.Empty;
            var weaveLight = combat != null && combat.Light > 0d;
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
            // The server's personal weave limit is distinct from general mana/skill capacity.
            // Avoid knowingly sending a pattern this body cannot currently hold; the server
            // remains authoritative and older frames without the field stay compatible.
            var weave = Obj(_me, "weave");
            var personalLimit = Num(weave, "limit");
            if (personalLimit > 0f)
            {
                int complexity, cost;
                bool closed;
                ReadPattern(out complexity, out cost, out closed);
                if (complexity > personalLimit)
                {
                    Toast("Узор слишком сложен для тебя сейчас (предел " +
                        personalLimit.ToString("0.#", CultureInfo.InvariantCulture) + "). Учись у мага или в месте силы.", 3f);
                    return;
                }
            }
            CoreFoeView foe;
            string part;
            float accuracy, distance;
            var aimed = AimFoe(out foe, out part, out accuracy, out distance);
            if (!aimed && !string.IsNullOrEmpty(_lockedFoeId))
            {
                Toast("Цель закрыта препятствием — выбери другую позицию.", 1.8f);
                return;
            }
            var target = aimed ? foe.FoeId : string.Empty;
            Ask("Плетение", "weave", "nodes", _pattern.ToArray(), "target", target);
        }

        private static Vector2 NodeCenter(int index)
        {
            var angle = -Mathf.PI / 2f + index * (Mathf.PI * 2f / Nodes.Length);
            const float radius = 180f;
            return new Vector2(Screen.width * 0.5f + Mathf.Cos(angle) * radius, Screen.height * 0.5f + Mathf.Sin(angle) * radius);
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
            int heat = 0, chill = 0, veil = 0, eye = 0;
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
                    case "heat": heat++; break;
                    case "chill": chill++; break;
                    case "veil": veil++; break;
                    case "eye": eye++; break;
                }
            }
            closed = _pattern.Count > 0 && _pattern[0] == "knot" && _pattern[_pattern.Count - 1] == "seal" && seal == 1;
            complexity = _pattern.Count + fork + (closed ? 0 : 3);
            cost = 4 * _pattern.Count + 4 * knot;
            // Preview mirrors the server's ReadWeave priority; the server alone resolves the cast.
            if (barb > 0)
                return "ранит" + (thread > 0 ? " издали" : " вблизи") +
                    (fork > 0 ? ", целей: " + (1 + fork) : "") +
                    (chill > 0 ? ", сковывает стужей" : "") +
                    (heat > 0 ? ", оставляет жгучий след" : loop > 0 ? ", потом жжёт" : "");
            if (veil > 0) return "отводит глаза врагам";
            if (eye > 0) return "чует угрозы и ближайшие дикие места";
            if (loop > 0 && thread > 0) return "заживляет раны" + (heat > 0 ? " тёплым жаром" : "");
            if (loop > 0) return fork > 0 ? "щит на отряд" : "щит" + (chill > 0 ? " с ледяной коркой" : "");
            if (thread > 0) return "отбрасывает и оглушает" + (chill > 0 ? " стужей" : "");
            if (heat > 0) return "возвращает силы теплом";
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
                var shortcut = i == 9 ? "0" : (i + 1).ToString(CultureInfo.InvariantCulture);
                GUI.Box(new Rect(c.x - 44f, c.y - 30f, 88f, 60f), NodeGlyph[i] + "\n" + shortcut + " " + NodeRu[i]);
            }

            var weave = Obj(_me, "weave");
            var capacity = Num(weave, "capacity");
            var glyphs = string.Empty;
            foreach (var node in _pattern) glyphs += NodeGlyph[Array.IndexOf(Nodes, node)] + " ";
            var text = _pattern.Count == 0 ? "Веди мышью по 10 узлам или выбирай 1–9, 0. Начни узлом, закончи печатью." : glyphs;
            if (_pattern.Count > 0)
            {
                int complexity, cost;
                bool closed;
                var what = ReadPattern(out complexity, out cost, out closed);
                var over = complexity - capacity;
                var risk = over <= -1f ? "удержишь" : over <= 1f ? "на пределе" : "надорвёшься";
                var personalLimit = Num(weave, "limit");
                var personal = personalLimit > 0f
                    ? " · предел тела " + personalLimit.ToString("0.#", CultureInfo.InvariantCulture)
                    : "";
                var inaccessible = personalLimit > 0f && complexity > personalLimit;
                text += "\nСделает: " + what + " · сложность " + complexity + " из " + capacity.ToString("0.#", CultureInfo.InvariantCulture)
                    + " (" + risk + ")" + personal + (inaccessible ? " · ПОКА НЕДОСТУПНО" : "")
                    + " · маны " + cost + (closed ? "" : " · узор НЕ замкнут");
                if (inaccessible)
                    text += "\nНужно учиться у мага или в месте силы; сервер не примет этот узор.";
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
            ApplyUiTheme();
            if (!_active || _me == null) return;

            if (_hurtFlash > 0f)
            {
                var previous = GUI.color;
                GUI.color = new Color(0.6f, 0.05f, 0.03f, Mathf.Clamp01(_hurtFlash) * 0.45f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            var journey = _journey;
            if (journey != null)
            {
                DrawRoad(journey);
                DrawFeed();
                return;
            }

            DrawFoeLabels();
            DrawLockedTargetMarker();
            DrawStatus();
            DrawWorldContextAction();
            DrawFeed();
            DrawQuestTracker();
            if (_hitMarker > 0f)
                GUI.Label(new Rect(Screen.width * 0.5f - 10f, Screen.height * 0.5f - 34f, 40f, 24f), "x");
            DrawFloaters();
            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime < _toastUntil)
                DrawToast();
            if (_weaveOpen) DrawWeave();
            if (_menuOpen) DrawMenu();
        }

        private void DrawToast()
        {
            if (_toastStyle == null)
            {
                _toastStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = ClientUiTypography.HudBody,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    clipping = TextClipping.Overflow,
                    padding = new RectOffset(12, 12, 8, 8)
                };
                _toastStyle.normal.textColor = new Color(0.98f, 0.93f, 0.83f, 1f);
            }

            var width = Mathf.Min(620f, Mathf.Max(180f, Screen.width - 32f));
            var content = new GUIContent(_toast);
            var height = Mathf.Max(38f, _toastStyle.CalcHeight(content, width - 24f) + 12f);
            var maxHeight = Mathf.Max(56f, Screen.height * 0.28f);
            height = Mathf.Min(height, maxHeight);
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.58f, width, height);
            var previous = GUI.color;
            GUI.color = new Color(0.10f, 0.085f, 0.065f, 0.94f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(rect, content, _toastStyle);
            GUI.color = previous;
        }

        private void DrawWorldContextAction()
        {
            if (_menuOpen || _weaveOpen || _me == null) return;

            // Show one concise action at the point of work; the server still validates every intent.
            // The full activity list remains available in the Дела window.
            string title = null;
            string primaryLabel = null;
            string primaryAction = null;
            string secondaryLabel = null;
            string secondaryAction = null;

            if (HasFeature("garden") && Near("apiary", 5f))
            {
                title = "Пасека";
                primaryLabel = "Вынуть мёд из улья";
                primaryAction = "apiary";
            }
            else if (HasFeature("woods") && Near("woods", 10f))
            {
                title = "Лесная делянка";
                primaryLabel = "Срубить дерево";
                primaryAction = "chop";
            }
            else if (HasFeature("river") && Near("river", 10f))
            {
                title = "Берег реки";
                primaryLabel = "Закинуть удочку";
                primaryAction = "fish";
            }
            else if (HasFeature("mine") && Near("mine", 10f))
            {
                title = "Рудный выход";
                primaryLabel = "Добыть руду";
                primaryAction = "mine";
            }
            else if (HasFeature("garden") && Near("garden", 10f))
            {
                title = "Огород и поля";
                primaryLabel = "Собрать травы";
                primaryAction = "gather";
                secondaryLabel = "Помочь в поле";
                secondaryAction = "farmwork";
            }
            else if (Near("smithy", 10f))
            {
                title = "Кузница";
                primaryLabel = "Помочь у горна";
                primaryAction = "forge";
            }

            if (string.IsNullOrEmpty(primaryAction)) return;

            const float width = 244f;
            const float rowHeight = 27f;
            var rows = string.IsNullOrEmpty(secondaryAction) ? 1 : 2;
            var height = 31f + rows * rowHeight + 8f;
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - 126f - (rows - 1) * 29f, width, height);
            GUI.Box(rect, title);
            if (GUI.Button(new Rect(rect.x + 8f, rect.y + 24f, rect.width - 16f, rowHeight), primaryLabel))
                AskActivity(title, primaryAction);
            if (!string.IsNullOrEmpty(secondaryAction) &&
                GUI.Button(new Rect(rect.x + 8f, rect.y + 24f + rowHeight, rect.width - 16f, rowHeight), secondaryLabel))
                AskActivity(title, secondaryAction);
        }

        private bool IsWorldPointOccluded(Camera cam, Vector3 point, Transform ownerRoot)
        {
            if (cam == null) return false;
            var offset = point - cam.transform.position;
            var distance = offset.magnitude;
            if (distance <= 0.05f) return false;
            var count = Physics.RaycastNonAlloc(cam.transform.position, offset / distance, _visibilityHits,
                distance - 0.04f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // A saturated NonAlloc buffer may omit an intervening wall; fail closed for lock-on visibility.
            if (count >= _visibilityHits.Length) return true;
            for (var i = 0; i < count; i++)
            {
                var collider = _visibilityHits[i].collider;
                if (collider == null) continue;
                var hit = collider.transform;
                if (ownerRoot != null && (hit == ownerRoot || hit.IsChildOf(ownerRoot))) continue;
                if (_player != null && (hit == _player || hit.IsChildOf(_player))) continue;
                return true;
            }
            return false;
        }

        private void DrawLockedTargetMarker()
        {
            if (string.IsNullOrEmpty(_lockedFoeId)) return;
            FoeRec rec;
            if (!_foes.TryGetValue(_lockedFoeId, out rec) || rec.Root == null || rec.View == null || rec.View.Dead)
                return;
            var cam = Camera.main;
            if (cam == null) return;
            var point = rec.Root.transform.position + Vector3.up * Mathf.Max(0.8f, rec.View.Height * 0.55f);
            if (IsWorldPointOccluded(cam, point, rec.Root.transform)) return;
            var screen = cam.WorldToScreenPoint(point);
            if (screen.z <= 0f) return;
            var x = screen.x;
            var y = Screen.height - screen.y;
            var previous = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.35f, 0.95f);
            GUI.Box(new Rect(x - 17f, y - 17f, 34f, 34f), "+");
            GUI.Label(new Rect(x - 72f, y - 42f, 144f, 20f), "ЦЕЛЬ · " + rec.View.DisplayName);
            GUI.color = previous;
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
                var labelPoint = rec.Root.transform.position + Vector3.up * (rec.View.Height + 0.35f);
                if (IsWorldPointOccluded(cam, labelPoint, rec.Root.transform)) continue;
                var screen = cam.WorldToScreenPoint(labelPoint);
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
                var worldPoint = floater.World + Vector3.up * (age * 0.9f);
                if (IsWorldPointOccluded(cam, worldPoint, floater.OwnerRoot)) continue;
                var screen = cam.WorldToScreenPoint(worldPoint);
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
            // Quiet survival readout: small, unlabeled bars keep the world unobstructed.
            // Detailed combat state and keybinds remain available on demand with H.
            const float width = 210f;
            var x = Screen.width * 0.5f - width * 0.5f;
            var y = Screen.height - 54f;
            var status = _status ?? GameplayStatusView.Empty;
            Bar(new Rect(x, y, width, 5f), status.Health / 100f, new Color(0.55f, 0.66f, 0.42f));
            Bar(new Rect(x, y + 8f, width, 4f), status.Stamina, new Color(0.85f, 0.76f, 0.42f));
            var mana = status.Mana;
            if (mana > 0f)
                Bar(new Rect(x, y + 15f, width, 4f), mana / 100f, new Color(0.5f, 0.65f, 0.88f));

            var drunk = Mathf.Clamp(status.Drunk, 0f, 100f);
            if (drunk > 0f)
            {
                Bar(new Rect(x, y + 22f, width, 3f), drunk / 100f, new Color(0.78f, 0.55f, 0.28f));
                GUI.Label(new Rect(x + width + 6f, y + 15f, 90f, 18f),
                    "Хмель " + drunk.ToString("0", CultureInfo.InvariantCulture) + "%");
            }

            var combat = _combat;
            var weapon = combat?.Weapon ?? string.Empty;
            var stance = combat?.Stance ?? string.Empty;
            var guard = combat != null && combat.Guard;
            var sneak = combat != null && combat.Sneak;
            var feint = combat != null && combat.Feint;
            var shield = combat != null ? (float)combat.Shield : 0f;
            var combatIsNotable = !string.IsNullOrEmpty(weapon) || guard || sneak || feint ||
                shield > 0f || (stance.Length > 0 && stance != "balanced");
            if (combatIsNotable)
            {
                var line = "В руках: " + (string.IsNullOrEmpty(weapon) ? "оружие не выбрано" : Name(weapon))
                    + (weapon == "bow" ? " · стрел " + (combat != null ? combat.Arrows : 0) : "")
                    + (stance == "aggressive" ? " · яростная стойка" : stance == "defensive" ? " · осторожная стойка" : "")
                    + (guard ? " · блок" : "") + (sneak ? " · скрытность" : "")
                    + (feint ? " · финт" : "") + (shield > 0f ? " · щит " + (int)shield : "");
                GUI.Label(new Rect(x - 150f, y - 24f, width + 300f, 24f), line);
            }

            if (_showHints)
            {
                EnsureHudTextStyles();
                var hintRect = new Rect(12f, Screen.height - 66f, Mathf.Min(390f, Screen.width - 24f), 54f);
                GUI.Box(hintRect, GUIContent.none, GUI.skin.box);
                GUI.Label(new Rect(hintRect.x + 8f, hintRect.y + 4f, hintRect.width - 16f, 22f),
                    "WASD — ходить · мышь — смотреть · E — взаимодействовать", _hintTextStyle);
                GUI.Label(new Rect(hintRect.x + 8f, hintRect.y + 27f, hintRect.width - 16f, 22f),
                    "Shift — бег · Alt — лёгкий бег · Space — прыжок · Tab — дела · H — скрыть", _hintTextStyle);
            }
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

        private void EnsureHudTextStyles()
        {
            if (_questTitleStyle == null)
            {
                _questTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = false
                };
            }
            if (_questBodyStyle == null)
            {
                _questBodyStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true
                };
            }
            if (_hintTextStyle == null)
            {
                _hintTextStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true
                };
            }
        }

        private void DrawQuestTracker()
        {
            if (_me == null || _menuOpen) return;
            var task = Obj(_me, "task");
            var issued = Obj(task, "issued");
            string title;
            string objective;
            var ready = false;

            if (issued != null && !Flag(task, "errand_done"))
            {
                var good = Str(issued, "good");
                var have = Mathf.Max(0f, Num(issued, "have"));
                var qty = Mathf.Max(0f, Num(issued, "qty"));
                ready = Flag(issued, "ready");
                var giver = Str(issued, "giver_name");
                title = string.IsNullOrEmpty(giver) ? "ПОРУЧЕНИЕ" : "ПОРУЧЕНИЕ · " + giver;
                objective = "Принести: " + Name(good) + " · " +
                    Mathf.Min((int)have, (int)qty) + "/" + (int)qty;
                var sourceSettlement = Str(issued, "settlement_name");
                if (!string.IsNullOrWhiteSpace(sourceSettlement))
                    objective += " · взято: " + sourceSettlement;
                if (ready) objective += " · можно сдавать";
            }
            else
            {
                var active = _quests?.Active;
                if (active == null || active.Count == 0) return;
                var quest = active[0];
                var kind = quest.Kind ?? string.Empty;
                var progress = kind == "deliver" || kind == "cull"
                    ? " · " + Mathf.Min(quest.Have ?? 0, quest.Quantity) +
                      "/" + quest.Quantity
                    : string.Empty;
                title = "ЗАДАЧА";
                ready = quest.Ready == true;
                objective = (ready ? "Готово · " : "") + (quest.Text ?? string.Empty) + progress;
            }

            EnsureHudTextStyles();
            var width = Mathf.Min(460f, Screen.width - 24f);
            var rect = new Rect(Screen.width - width - 12f, 12f, width, 68f);
            var previousColor = GUI.color;
            GUI.color = ready ? new Color(0.34f, 0.30f, 0.20f, 0.96f) : new Color(0.10f, 0.13f, 0.12f, 0.94f);
            GUI.Box(rect, GUIContent.none, GUI.skin.box);
            GUI.color = previousColor;
            GUI.Label(new Rect(rect.x + 10f, rect.y + 5f, rect.width - 20f, 20f), title, _questTitleStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 26f, rect.width - 20f, 38f), objective, _questBodyStyle);
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

        private void DrawRoad(JourneyFrame journey)
        {
            var width = Mathf.Min(680f, Screen.width - 40f);
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f - 170f, width, 340f);
            GUI.Box(rect, "Дорога в " + (journey.ToName ?? string.Empty));
            GUILayout.BeginArea(new Rect(rect.x + 16f, rect.y + 30f, rect.width - 32f, rect.height - 40f));
            Bar(GUILayoutUtility.GetRect(rect.width - 32f, 12f), (float)journey.Progress, new Color(0.79f, 0.64f, 0.29f));
            GUILayout.Space(8f);
            GUILayout.Label(journey.Scene ?? string.Empty);
            var note = journey.Note;
            if (!string.IsNullOrEmpty(note)) GUILayout.Label(note);
            var roadEvent = journey.Event;
            if (roadEvent != null)
            {
                GUILayout.Space(8f);
                GUILayout.Label(roadEvent.Text ?? string.Empty);
                if (journey.Follow)
                {
                    GUILayout.Label("Решает вожак отряда…");
                }
                else
                {
                    var options = roadEvent.Options;
                    for (var i = 0; options != null && i < options.Count; i++)
                    {
                        var option = options[i];
                        if (GUILayout.Button("[" + (i + 1) + "] " + OptionRu(option), GUILayout.Height(30f)))
                            Ask("Выбор пути", "journey", "action", "choose", "choice", option);
                    }
                }
            }
            else
            {
                GUILayout.Label("Ещё примерно " + (int)journey.Eta + " с пути.");
            }
            GUILayout.EndArea();
        }

        public bool SupportsFeature(string feature) => HasFeature(feature);

        private bool HasFeature(string feature)
        {
            var site = Obj(_place, "site");
            var features = Arr(site, "features");
            if (features == null) return true; // older frame: preserve legacy affordances
            foreach (var value in features)
                if (string.Equals(value.ToString(), feature, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string TerrainName(string terrain)
        {
            switch (terrain)
            {
                case "river": return "речная долина";
                case "hills": return "холмы";
                case "forest": return "лесная местность";
                case "plain": return "равнина";
                case "coast": return "побережье";
                case "marsh": return "болота";
                default: return string.IsNullOrEmpty(terrain) ? "неизвестная местность" : terrain;
            }
        }

        private static string ClimateName(string climate)
        {
            switch (climate)
            {
                case "polar": return "полярный";
                case "cold": return "холодный";
                case "temperate": return "умеренный";
                case "warm": return "тёплый";
                case "hot": return "жаркий";
                default: return string.IsNullOrEmpty(climate) ? "неизвестен" : climate;
            }
        }

        private static string TimeOfDayName(int hour)
        {
            var normalized = ((hour % 24) + 24) % 24;
            if (normalized < 5) return "глубокая ночь";
            if (normalized < 8) return "рассвет";
            if (normalized < 11) return "утро";
            if (normalized < 17) return "день";
            if (normalized < 20) return "вечер";
            if (normalized < 22) return "сумерки";
            return "ночь";
        }

        private static string FeatureName(string feature)
        {
            switch (feature)
            {
                case "river": return "река";
                case "woods": return "лес";
                case "mine": return "рудник";
                case "garden": return "поля";
                default: return feature;
            }
        }

        private bool Near(string poi, float radius)
        {
            var pos = Obj(_me, "pos");
            if (pos == null || !PoiPositionPolicy.TryResolve(
                poi, _layoutPois, Poi, _hasLayoutPois, _allowLegacyPoiFallback, out var anchor))
                return false;

            return PoiPositionPolicy.IsWithin(
                Num(pos, "x"), Num(pos, "y"), anchor.X, anchor.Y, radius);
        }

        private bool NearTavernCounter()
        {
            var pos = Obj(_me, "pos");
            if (_tavernCounter == null || pos == null || _tavernCounter.Radius <= 0d)
                return false;
            return PoiPositionPolicy.IsWithin(
                Num(pos, "x"), Num(pos, "y"),
                _tavernCounter.X, _tavernCounter.Y, _tavernCounter.Radius);
        }

        private static float Count(JObject bag, string key)
        {
            return bag == null ? 0f : Num(bag, key);
        }

        /// <summary>Shows a small live pulse from the server's currently replicated local neighbors.</summary>
        private void DrawSettlementPulse()
        {
            var life = Obj(_place, "life");
            if (life != null)
            {
                GUILayout.Label("ЖИЗНЬ ПОСЕЛЕНИЯ · общая сводка");
                GUILayout.Label("Жителей: " + (int)Num(life, "population") +
                    " · заняты делом: " + (int)Num(life, "working"));

                var activities = Obj(life, "activities");
                var activityDetails = new System.Collections.Generic.List<string>();
                AddLifeCount(activityDetails, activities, "field", "поле");
                AddLifeCount(activityDetails, activities, "maintenance", "ремонт снастей");
                AddLifeCount(activityDetails, activities, "wood", "лес");
                AddLifeCount(activityDetails, activities, "forge", "кузница");
                AddLifeCount(activityDetails, activities, "mine", "рудник");
                AddLifeCount(activityDetails, activities, "fish", "рыбаки");
                AddLifeCount(activityDetails, activities, "hunt", "охота");
                AddLifeCount(activityDetails, activities, "herb", "травы");
                AddLifeCount(activityDetails, activities, "cook", "кухня");
                AddLifeCount(activityDetails, activities, "herd", "стадо");
                AddLifeCount(activityDetails, activities, "magic", "плетения");
                AddLifeCount(activityDetails, activities, "tavern", "корчма");
                AddLifeCount(activityDetails, activities, "rest", "отдых");
                AddLifeCount(activityDetails, activities, "social", "общение");
                AddLifeCount(activityDetails, activities, "eating", "едят");
                AddLifeCount(activityDetails, activities, "other", "прочее");
                if (activityDetails.Count > 0)
                    GUILayout.Label("Занятия: " + string.Join(" · ", activityDetails.ToArray()));

                var stock = Obj(life, "stock");
                if (stock != null)
                {
                    var stockDetails = new System.Collections.Generic.List<string>();
                    AddLifeStock(stockDetails, stock, "wood", "дрова");
                    AddLifeStock(stockDetails, stock, "ore", "руда");
                    AddLifeStock(stockDetails, stock, "metal", "металл");
                    AddLifeStock(stockDetails, stock, "herb", "травы");
                    if (stockDetails.Count > 0)
                        GUILayout.Label("Общие запасы: " + string.Join(" · ", stockDetails.ToArray()));
                }
            }

            var neighbors = Arr(_place, "neighbors");
            if (neighbors != null)
            {
                var residents = 0;
                var players = 0;
                foreach (var neighbor in neighbors)
                {
                    if (Flag(neighbor, "player")) players++;
                    else residents++;
                }
                GUILayout.Label("Рядом в поле зрения: " + residents + " жителей" +
                    (players > 0 ? " · игроков: " + players : ""));
            }

            var details = new System.Collections.Generic.List<string>();
            if (Num(_place, "food") > 0) details.Add("еда: " + Num(_place, "food").ToString("0.#", CultureInfo.InvariantCulture));
            if (Flag(_place, "tavern")) details.Add("пиво в корчме: " + Num(_place, "beer").ToString("0.#", CultureInfo.InvariantCulture));
            var stable = Obj(_place, "stable");
            if (stable != null) details.Add("лошадей: " + (int)Num(stable, "horses"));
            var market = Arr(_place, "market");
            if (market != null && market.Count > 0) details.Add("торговых позиций: " + market.Count);
            if (details.Count > 0) GUILayout.Label(string.Join(" · ", details.ToArray()));
        }

        private static void AddLifeCount(System.Collections.Generic.List<string> target, JObject activities, string key, string label)
        {
            var count = (int)Num(activities, key);
            if (count > 0) target.Add(label + ": " + count);
        }

        private static void AddLifeStock(System.Collections.Generic.List<string> target, JObject stock, string key, string label)
        {
            var amount = Num(stock, key);
            if (amount > 0) target.Add(label + ": " + amount.ToString("0.#", CultureInfo.InvariantCulture));
        }

        /// <summary>Compact, discoverable market controls shown at the physical market stall.</summary>
        public void DrawMarketContextActions()
        {
            if (!_active || _me == null || _place == null) return;
            var market = Arr(_place, "market");
            if (market == null || market.Count == 0)
            {
                GUILayout.Label("Лавка: ассортимент пока не поступил.");
                return;
            }

            var coins = Num(_me, "coins");
            var stash = Obj(_me, "stash");
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("ЛАВКА · МОНЕТЫ: " + coins.ToString("0.#", CultureInfo.InvariantCulture));
            GUILayout.Label("Полный ассортимент из place.market. Покупка кладёт товар в поклажу; продажа забирает его из поклажи. Каждая операция подтверждается сервером.");
            _marketRowsScroll = GUILayout.BeginScrollView(_marketRowsScroll,
                GUILayout.Height(Mathf.Min(300f, Mathf.Max(120f, Screen.height * 0.34f))));
            foreach (var row in market)
            {
                var good = Str(row, "good");
                if (string.IsNullOrEmpty(good)) continue;
                var stock = Num(row, "stock");
                var buyPrice = Num(row, "price");
                var sellPrice = Num(row, "sell");
                var owned = Count(stash, good);
                GUILayout.BeginHorizontal();
                GUILayout.Label(Name(good) + " · запас " + stock.ToString("0.#", CultureInfo.InvariantCulture) +
                    " · у тебя " + owned.ToString("0.#", CultureInfo.InvariantCulture), GUILayout.MinWidth(210f));
                var previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && stock >= 1f && coins >= buyPrice;
                if (GUILayout.Button("Купить · " + buyPrice.ToString("0.#", CultureInfo.InvariantCulture),
                    GUILayout.Width(118f)))
                    Ask("Покупка " + Name(good), "trade", "good", good, "side", "buy", "qty", 1);
                GUI.enabled = previousEnabled && owned >= 1f;
                if (GUILayout.Button("Продать · " + sellPrice.ToString("0.#", CultureInfo.InvariantCulture),
                    GUILayout.Width(118f)))
                    Ask("Продажа " + Name(good), "trade", "good", good, "side", "sell", "qty", 1);
                GUI.enabled = previousEnabled;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawMenu()
        {
            var width = Mathf.Min(900f, Screen.width - 40f);
            var height = Mathf.Min(680f, Screen.height - 40f);
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(rect, "Персонаж");
            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 28f, rect.width - 28f, rect.height - 38f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Информация, поклажа и занятия", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("× Закрыть", GUILayout.Width(110f)))
            {
                _menuOpen = false;
                _holding = false;
                ReleaseGuard();
                UpdateCursor(_externalCharacterMenuOpen || _weaveOpen);
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            _menuScroll = GUILayout.BeginScrollView(_menuScroll);

            var inv = Obj(_me, "inv");
            var items = Obj(inv, "items");
            var worn = Obj(inv, "worn");
            var hand = Str(inv, "hand");
            var stash = Obj(_me, "stash");
            var coins = Num(_me, "coins");

            GUILayout.BeginHorizontal();
            GUILayout.Label("МОНЕТЫ ПРИ СЕБЕ: " + coins.ToString("0.#", CultureInfo.InvariantCulture));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label("ЗДЕСЬ");
            if (_place != null)
            {
                var tier = Str(_place, "tier");
                var tierName = tier == "city" ? "город" : tier == "village" ? "деревня" : tier;
                GUILayout.Label(Str(_place, "name") + (string.IsNullOrEmpty(tierName) ? "" : " · " + tierName));
                GUILayout.Label("Местное время: " + _hour.ToString("00", CultureInfo.InvariantCulture) +
                    ":00 · " + TimeOfDayName(_hour));
            }
            var site = Obj(_place, "site");
            if (site != null)
            {
                var have = new System.Collections.Generic.List<string>();
                var features = Arr(site, "features");
                if (features != null)
                    foreach (var feature in features)
                    {
                        var featureName = FeatureName(feature.ToString());
                        if (!string.IsNullOrEmpty(featureName)) have.Add(featureName);
                    }
                GUILayout.Label("Местность: " + TerrainName(Str(site, "terrain")) +
                    " · климат: " + ClimateName(Str(site, "climate")));
                GUILayout.Label("Особенность: " + Str(site, "specialty") +
                    " · держава: " + Str(site, "state"));
                if (have.Count > 0)
                    GUILayout.Label("Угодья поблизости: " + string.Join(" · ", have.ToArray()));
            }
            DrawSettlementPulse();
            DrawWildSites();
            if (_place != null && Flag(_place, "tavern") && Near("tavern", 12f))
            {
                if (Flag(_me, "mug"))
                {
                    RequestButton("Выпить", "Выпить", "consume", "item", "beer");
                }
                else if (_tavernCounter == null)
                {
                    GUILayout.Label("Стойка корчмы пока не описана сервером.");
                }
                else if (!NearTavernCounter())
                {
                    GUILayout.Label("Покупка пива доступна у стойки внутри корчмы.");
                }
                else if (!_tavernCounter.BartenderPresent)
                {
                    GUILayout.Label("Корчмарь сейчас не на смене.");
                }
                else if (Num(_place, "beer") <= 0f)
                {
                    GUILayout.Label("Пиво закончилось.");
                }
                else if (coins < Num(_place, "beer_price"))
                {
                    GUILayout.Label("Не хватает монет на кружку.");
                }
                else
                {
                    RequestButton("Заказать пива (" +
                        Num(_place, "beer_price").ToString("0.#", CultureInfo.InvariantCulture) + " монеты)",
                        "Пиво", "buy", "item", "beer");
                }
            }

            DrawCardGames();
            if (HasFeature("mine") && Near("mine", 10f) && GUILayout.Button("Добыть руду")) AskActivity("Руда", "mine");
            if (HasFeature("garden") && Near("garden", 10f) && GUILayout.Button("Собрать травы")) AskActivity("Травы", "gather");
            if (HasFeature("garden") && Near("garden", 10f) && GUILayout.Button("🌾 Помочь в поле")) AskActivity("Работа в поле", "farmwork");
            if (HasFeature("garden") && Near("apiary", 5f) && GUILayout.Button("🍯 Вынуть мёд на пасеке")) AskActivity("Пасека", "apiary");
            if (Near("smithy", 10f) && GUILayout.Button("⚒ Помочь у горна")) AskActivity("Работа у горна", "forge");
            if (Flag(Obj(_me, "weave"), "can_learn") && GUILayout.Button("Созерцать ману (учиться плетению)")) AskActivity("Созерцание", "meditate");
            if (Flag(Obj(_me, "weave"), "can_study") && GUILayout.Button("Учиться у мага рядом (новый узор)")) AskActivity("Урок плетения", "study");
            if (HasFeature("woods") && Near("woods", 10f) && GUILayout.Button("Срубить дерево")) AskActivity("Рубка", "chop");
            if (HasFeature("river") && Near("river", 10f) && GUILayout.Button("Закинуть удочку")) AskActivity("Рыбалка", "fish");
            var catalogItemsForSale = Arr(_catalog, "items");
            if (catalogItemsForSale != null)
            {
                foreach (var definition in catalogItemsForSale)
                {
                    var item = Str(definition, "item");
                    var at = Str(definition, "at");
                    var price = Num(definition, "price");
                    if (string.IsNullOrEmpty(item) || string.IsNullOrEmpty(at) || price <= 0 ||
                        !Near(at, 10f) || Count(items, item) >= 1f) continue;
                    var previousEnabled = GUI.enabled;
                    GUI.enabled = previousEnabled && coins >= price;
                    if (GUILayout.Button("Купить: " + CatalogItemName(definition, item) + " · " +
                        price.ToString("0.#", CultureInfo.InvariantCulture) + " мон."))
                        Ask("Покупка", "take", "item", item);
                    GUI.enabled = previousEnabled;
                }
            }
            else
            {
                foreach (var pair in TakeAt)
                {
                    if (!Near(pair.Value, 10f) || Count(items, pair.Key) >= 1f) continue;
                    var price = TakePrice.TryGetValue(pair.Key, out var itemPrice) ? itemPrice : int.MaxValue;
                    GUI.enabled = price != int.MaxValue && coins >= price;
                    if (GUILayout.Button("Купить: " + Name(pair.Key) + " · " + (price == int.MaxValue ? "цена не задана" : price + " мон.")))
                        Ask("Покупка", "take", "item", pair.Key);
                    GUI.enabled = true;
                }
            }

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
                    var previousEnabled = GUI.enabled;
                    var buyPrice = Num(row, "price");
                    var canBuy = Num(row, "stock") >= 1f && coins >= buyPrice;
                    GUI.enabled = previousEnabled && canBuy;
                    if (GUILayout.Button("купить " + buyPrice.ToString("0.#", CultureInfo.InvariantCulture)))
                        Ask("Покупка", "trade", "good", good, "side", "buy", "qty", 1);
                    GUI.enabled = previousEnabled && Count(stash, good) >= 1f;
                    if (GUILayout.Button("продать " + Num(row, "sell").ToString("0.#", CultureInfo.InvariantCulture)))
                        Ask("Продажа", "trade", "good", good, "side", "sell", "qty", 1);
                    GUI.enabled = previousEnabled;
                    if (Num(row, "stock") >= 1f && coins < buyPrice)
                        GUILayout.Label("Не хватает монет");
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
            var bagCapacity = _bag;
            GUILayout.Label("СУМА: " + (string.IsNullOrEmpty(bagLine) ? "пусто" : bagLine)
                + (bagCapacity != null ? "   ·   " + (int)bagCapacity.Load + "/" + (int)bagCapacity.Capacity : ""));

            var saddleContents = _horse?.Bag;
            var saddleLine = string.Empty;
            if (saddleContents != null)
                foreach (var good in saddleContents)
                    if (good.Value >= 1d)
                        saddleLine += Name(good.Key) + " ×" + (int)good.Value + "   ";
            GUILayout.Label("СЕДЕЛЬНАЯ СУМКА: " + (string.IsNullOrEmpty(saddleLine) ? "пусто" : saddleLine));
            GUILayout.BeginHorizontal();
            foreach (var item in new[] { "axe", "sword", "knife", "bow", "rod", "torch", "lantern" })
                if (Count(items, item) > 0f && hand != item && GUILayout.Button("в руку: " + Name(item))) Ask("Экипировка", "equip", "item", item);
            if (!string.IsNullOrEmpty(hand) && GUILayout.Button("убрать из руки")) Ask("Экипировка", "equip", "item", string.Empty);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var item in new[] { "cloak", "boots", "hat", "jerkin", "shield", "purse" })
            {
                var already = false;
                if (worn != null)
                    foreach (var slot in worn.Properties())
                        if (slot.Value.ToString() == item) already = true;
                if (Count(items, item) > 0f && !already && GUILayout.Button("надеть: " + Name(item))) Ask("Одежда", "wear", "item", item);
                if (already && GUILayout.Button("снять: " + Name(item))) Ask("Одежда", "unwear", "item", item);
            }
            foreach (var item in new[] { "stew", "roast", "potion", "bandage" })
                if (Count(items, item) > 0f && GUILayout.Button("употребить: " + Name(item))) Ask("Употребить", "use", "item", item);
            foreach (var good in new[] { "bread", "fish", "meat", "cheese", "veg" })
                if (Count(stash, good) >= 1f && GUILayout.Button("съесть: " + Name(good))) Ask("Еда", "use", "item", good);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            var quests = _quests;
            GUILayout.Label("ПРОСЬБЫ  (выполнено " + (quests?.Done ?? 0) + ", слава здесь " + (quests?.Reputation ?? 0) + ")");
            var active = quests?.Active;
            if (active != null)
                foreach (var quest in active)
                {
                    var questReady = quest.Ready == true;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((questReady ? "[готово] " : "... ") + (quest.Text ?? string.Empty), GUILayout.Width(rect.width - 260f));
                    GUI.enabled = questReady;
                    if (GUILayout.Button("сдать")) Ask("Просьба", "quest", "action", "turnin", "quest", quest.Id);
                    GUI.enabled = true;
                    if (GUILayout.Button("отказаться")) Ask("Просьба", "quest", "action", "drop", "quest", quest.Id);
                    GUILayout.EndHorizontal();
                }
            var offers = quests?.Offers;
            var currentTalk = Obj(_me, "talk");
            var currentTalkWith = Str(currentTalk, "with");
            if (offers != null)
                foreach (var offer in offers)
                {
                    GUILayout.BeginHorizontal();
                    var via = offer.Via ?? string.Empty;
                    var giver = offer.Giver ?? string.Empty;
                    var isCurrentNpc = via == "npc" && !string.IsNullOrEmpty(currentTalkWith) && giver == currentTalkWith;
                    var giverName = offer.GiverName ?? string.Empty;
                    var who = via == "board" ? "доска у колодца"
                        : isCurrentNpc ? "собеседник " + giverName
                        : giverName;
                    var pay = offer.Coins > 0d ? "  [" + (int)offer.Coins + " монет]" : string.Empty;
                    GUILayout.Label(who + ": " + (offer.Text ?? string.Empty) + pay, GUILayout.Width(rect.width - 210f));
                    if (via != "npc" || isCurrentNpc)
                    {
                        if (GUILayout.Button(isCurrentNpc ? "взяться у него" : "взяться"))
                            Ask("Просьба", "quest", "action", "accept", "quest", offer.Id);
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

            var stats = _stats;
            if (stats != null)
            {
                GUILayout.Space(8f);
                GUILayout.Label("КАКОВ ТЫ САМ (растёт от дела)");
                GUILayout.Label("сила " + stats.Strength.ToString("0.#", CultureInfo.InvariantCulture)
                    + "   ловкость " + stats.Agility.ToString("0.#", CultureInfo.InvariantCulture)
                    + "   выносливость " + stats.Endurance.ToString("0.#", CultureInfo.InvariantCulture)
                    + "   ум " + stats.Intellect.ToString("0.#", CultureInfo.InvariantCulture)
                    + "   восприятие " + stats.Perception.ToString("0.#", CultureInfo.InvariantCulture)
                    + "   воля " + stats.Will.ToString("0.#", CultureInfo.InvariantCulture));
                GUILayout.Label(Flag(Obj(_me, "weave"), "gift")
                    ? "В тебе есть дар: ум удержит и сложный узор."
                    : "Дара нет: по силам лишь самые простые узоры. Учиться можно в местах силы и у магов.");
            }
            var drunk = _status == null ? 0f : _status.Drunk;
            if (drunk > 0f) GUILayout.Label("Хмель: " + (int)drunk);
            var bagInfo = _bag;
            if (bagInfo != null)
                GUILayout.Label("Сума: " + (int)bagInfo.Load + " из " + (int)bagInfo.Capacity
                    + (bagInfo.Overloaded ? " — ПЕРЕГРУЗ: идёшь медленно" : ""));

            GUILayout.Space(8f);
            GUILayout.Label("ЛОШАДЬ · покупка у конюха, верховая езда и уход");
            var horse = _horse;
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
                GUILayout.Label(horse.Name + " · силы " + (int)horse.Stamina + " · седельная сумка "
                    + (int)horse.BagLoad + "/" + (int)horse.BagCapacity + (horse.Here ? "" : " · осталась в другом селе"));
                if (horse.Here)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(horse.Mounted ? "Слезть" : "В седло"))
                        Ask("Лошадь", "horse", "action", horse.Mounted ? "dismount" : "mount");
                    if (GUILayout.Button("Покормить")) Ask("Корм", "horse", "action", "feed");
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    if (stash != null)
                        foreach (var packed in stash.Properties())
                            if (Num(stash, packed.Name) >= 1f && GUILayout.Button("на лошадь: " + Name(packed.Name)))
                                Ask("Вьюк", "horse", "action", "stow", "good", packed.Name, "qty", 1);
                    var saddle = horse.Bag;
                    if (saddle != null)
                        foreach (var packed in saddle)
                            if (GUILayout.Button("с лошади: " + Name(packed.Key)))
                                Ask("Вьюк", "horse", "action", "unstow", "good", packed.Key, "qty", 1);
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

        private void UpdateCardGameHotkeys()
        {
            if (_input == null || _me == null || _place == null) return;
            var game = _cardGame;
            var gameName = game?.Game;
            var done = game == null || game.Done;
            var atTable = Flag(_place, "tavern") && Near("tavern", 12f);
            if (!atTable) return;

            if (game == null || done)
            {
                if (_input.JourneyChoicePressed(0) && Num(_me, "coins") >= 10f)
                    Ask("Очко", "card_game", "game", "ochko", "action", "start");
                else if (_input.JourneyChoicePressed(1) && Num(_me, "coins") >= 10f)
                    Ask("Дурак", "card_game", "game", "durak", "action", "start");
                return;
            }

            if (gameName == "ochko")
            {
                if (_input.JourneyChoicePressed(0))
                    Ask("Очко: взять карту", "card_game", "game", "ochko", "action", "hit");
                else if (_input.JourneyChoicePressed(1))
                    Ask("Очко: остановиться", "card_game", "game", "ochko", "action", "stand");
                return;
            }

            if (gameName != "durak" || !game.YourTurn) return;
            var role = game.Role;
            var phase = game.Phase;
            var hand = game.Player;
            if (hand != null && (role == "attack" && phase == "attack" ||
                role == "defend" && phase == "defend"))
            {
                for (var i = 0; i < hand.Count && i < 6; i++)
                {
                    if (!_input.JourneyChoicePressed(i)) continue;
                    Ask(role == "attack" ? "Дурак: атака" : "Дурак: отбивание",
                        "card_game", "game", "durak", "action",
                        role == "attack" ? "attack" : "defend", "index", i);
                    return;
                }
            }
            if (role == "defend" && phase == "defend" && _input.JourneyChoicePressed(6))
                Ask("Дурак: взять", "card_game", "game", "durak", "action", "take");
            else if (role == "attack" && phase == "attack" && DurakTableBeaten(game.Table) &&
                _input.JourneyChoicePressed(7))
                Ask("Дурак: бито", "card_game", "game", "durak", "action", "done");
        }

        /// <summary>
        /// v1.22 server-authored wilderness places. The client only offers actions listed by
        /// place.sites and never grants proximity, stock or outcomes locally.
        /// </summary>
        private void DrawWildSites()
        {
            var sites = Arr(_place, "sites");
            if (sites == null || sites.Count == 0) return;

            GUILayout.Space(8f);
            GUILayout.Label("ДИКИЕ МЕСТА ПОБЛИЗОСТИ");
            GUILayout.BeginVertical(GUI.skin.box);
            foreach (var site in sites)
            {
                if (site == null || string.IsNullOrEmpty(Str(site, "id"))) continue;
                var id = Str(site, "id");
                var name = Str(site, "name");
                var kind = Str(site, "kind");
                var here = Flag(site, "here");
                var distance = Num(site, "dist");
                var direction = Str(site, "dir");
                var gives = Str(site, "gives");
                GUILayout.BeginVertical();
                GUILayout.Label((string.IsNullOrEmpty(name) ? "Неизвестное место" : name) +
                    " · " + WildPlaceKindName(kind));
                if (!here)
                {
                    GUILayout.Label(direction + " · " + distance.ToString("0", CultureInfo.InvariantCulture) + " м");
                    if (GUILayout.Button("Подойти к месту", GUILayout.Height(28f)))
                        Ask("Путь к месту: " + name, "goto", "site", id);
                }
                else
                {
                    GUILayout.Label("Ты на месте" +
                        (string.IsNullOrEmpty(gives) ? "" : " · можно найти: " + CatalogName(gives)) +
                        (site["left"] == null ? "" : " · осталось: " + Num(site, "left").ToString("0", CultureInfo.InvariantCulture)));
                    var actions = Arr(site, "acts");
                    if (actions != null)
                    {
                        foreach (var actionToken in actions)
                        {
                            var action = actionToken == null ? string.Empty : actionToken.ToString();
                            if (string.IsNullOrEmpty(action)) continue;
                            var label = WildPlaceActionName(action, gives);
                            var previousEnabled = GUI.enabled;
                            if (action == "forage" && Num(site, "left") < 1f) GUI.enabled = false;
                            if (action == "forage" && gives == "fish" && Str(Obj(_me, "inv"), "hand") != "rod")
                            {
                                GUI.enabled = false;
                                GUILayout.Label("Для рыбалки нужна удочка в руке.");
                            }
                            var clicked = GUILayout.Button(label, GUILayout.Height(28f));
                            GUI.enabled = previousEnabled;
                            if (clicked) Ask(label, "site", "site", id, "action", action);
                        }
                    }
                }
                GUILayout.EndVertical();
                GUILayout.Space(4f);
            }
            GUILayout.EndVertical();
        }

        private static string WildPlaceKindName(string kind)
        {
            switch (kind)
            {
                case "camp": return "старая стоянка";
                case "spring": return "родник";
                case "berry": return "ягодник";
                case "herbs": return "травяной луг";
                case "thicket": return "бурелом / чаща";
                case "outcrop": return "рудная осыпь";
                case "pond": return "пруд";
                case "hut": return "хижина";
                case "barrow": return "курган";
                case "ruins": return "руины";
                case "den": return "логово";
                case "cave": return "пещера";
                case "tower": return "старая башня";
                case "grove": return "священная роща";
                default: return "урочище";
            }
        }

        private string WildPlaceActionName(string action, string gives)
        {
            switch (action)
            {
                case "rest": return "Передохнуть";
                case "drink": return "Напиться воды";
                case "explore": return "Исследовать / обыскать";
                case "forage": return "Собрать: " + (string.IsNullOrEmpty(gives) ? "припасы" : CatalogName(gives));
                default: return "Действие: " + action;
            }
        }

        private void DrawCardGames()
        {
            var game = _cardGame;
            var gameName = game?.Game;
            var done = game == null || game.Done;
            var atTable = _place != null && Flag(_place, "tavern") && Near("tavern", 12f);

            if (atTable)
            {
                GUILayout.Space(6f);
                GUILayout.Label("КАРТОЧНЫЙ СТОЛ");
                if (game == null || done)
                {
                    GUILayout.Label("Клавиши: 1 — очко · 2 — подкидной дурак");
                    GUILayout.BeginHorizontal();
                    var previousEnabled = GUI.enabled;
                    GUI.enabled = previousEnabled && Num(_me, "coins") >= 10f;
                    RequestButton("Сыграть в очко", "Очко", "card_game", "game", "ochko", "action", "start");
                    RequestButton("Сыграть в подкидного дурака", "Дурак", "card_game", "game", "durak", "action", "start");
                    GUI.enabled = previousEnabled;
                    GUILayout.EndHorizontal();
                }
            }

            if (game == null) return;
            GUILayout.BeginVertical(GUI.skin.box);
            if (gameName == "ochko")
            {
                GUILayout.Label("ОЧКО · кон " + game.Stake.ToString("0.#", CultureInfo.InvariantCulture));
                if (!game.Done) GUILayout.Label("Клавиши: 1 — ещё карту · 2 — хватит");
                DrawCardRow("Корчмарь", game.Dealer);
                if (!game.Done && game.HiddenDealerCards > 0)
                    GUILayout.Label("Скрытых карт у корчмаря: " + game.HiddenDealerCards);
                else if (game.DealerTotal.HasValue)
                    GUILayout.Label("Очки корчмаря: " + game.DealerTotal.Value);
                DrawCardRow("Твоя рука · " + game.PlayerTotal + " очков", game.Player);
                GUILayout.Label(game.Message ?? string.Empty);
                if (!game.Done && atTable)
                {
                    GUILayout.BeginHorizontal();
                    RequestButton("Ещё карту", "Очко: взять карту", "card_game", "game", "ochko", "action", "hit");
                    RequestButton("Хватит", "Очко: остановиться", "card_game", "game", "ochko", "action", "stand");
                    GUILayout.EndHorizontal();
                }
            }
            else if (gameName == "durak")
            {
                GUILayout.Label("ПОДКИДНОЙ ДУРАК · кон " + game.Stake.ToString("0.#", CultureInfo.InvariantCulture));
                GUILayout.Label("Клавиши 1–6 — выбрать карту · 7 — взять карты / 8 — закончить заход");
                GUILayout.Label("Козырь: " + (game.Trump ?? "—") + " · нижняя карта: " +
                    (string.IsNullOrEmpty(game.TrumpCard) ? "колода пуста" : game.TrumpCard) +
                    " · в колоде: " + game.DeckCount + " · у корчмаря: " + game.DealerCardCount);
                DrawDurakTable(game.Table);
                GUILayout.Label("Твоя роль: " + (game.Role == "attack" ? "атакующий" : "защитник") +
                    (game.YourTurn ? " · твой ход" : " · ход корчмаря"));
                DrawCardRow("Твои карты", game.Player);
                GUILayout.Label(game.Message ?? string.Empty);
                if (!game.Done && atTable && game.YourTurn)
                {
                    var role = game.Role;
                    var phase = game.Phase;
                    var hand = game.Player;
                    if (hand != null)
                    {
                        for (var i = 0; i < hand.Count; i++)
                        {
                            var code = hand[i]?.Code ?? "?";
                            if (role == "attack" && phase == "attack")
                            {
                                RequestButton("Подкинуть / зайти: " + code, "Дурак: атака",
                                    "card_game", "game", "durak", "action", "attack", "index", i);
                            }
                            else if (role == "defend" && phase == "defend")
                            {
                                RequestButton("Отбиться: " + code, "Дурак: отбивание",
                                    "card_game", "game", "durak", "action", "defend", "index", i);
                            }
                        }
                    }
                    if (role == "defend" && phase == "defend")
                        RequestButton("Взять карты", "Дурак: взять", "card_game", "game", "durak", "action", "take");
                    if (role == "attack" && phase == "attack" && DurakTableBeaten(game.Table))
                        RequestButton("Бито — закончить заход", "Дурак: бито", "card_game", "game", "durak", "action", "done");
                }
            }
            GUILayout.EndVertical();
        }

        private static void DrawCardRow(string title, List<CardGameCardFrame> cards)
        {
            GUILayout.Label(title);
            if (cards == null || cards.Count == 0)
            {
                GUILayout.Label("—");
                return;
            }
            var line = string.Empty;
            foreach (var card in cards)
            {
                if (card == null) continue;
                if (line.Length > 0) line += "   ";
                line += "[" + (card.Code ?? "?") + "]";
            }
            GUILayout.Label(line.Length == 0 ? "—" : line);
        }

        private static void DrawDurakTable(List<DurakTablePairFrame> table)
        {
            GUILayout.Label("На столе");
            if (table == null || table.Count == 0)
            {
                GUILayout.Label("Пусто");
                return;
            }
            foreach (var pair in table)
            {
                var attack = pair?.Attack?.Code ?? "?";
                var defence = pair?.Defence?.Code;
                GUILayout.Label("  " + attack + (string.IsNullOrEmpty(defence) ? "  →  ?" : "  →  " + defence));
            }
        }

        private static bool DurakTableBeaten(List<DurakTablePairFrame> table)
        {
            if (table == null || table.Count == 0) return false;
            foreach (var pair in table)
                if (pair == null || pair.Defence == null) return false;
            return true;
        }

        // Regional map as a list: server-known nodes only (me.region). Unknown road ends carry no name.
        private void DrawRegion(string hereId)
        {
            var nodes = _region?.Nodes;
            var roads = _region?.Roads;
            if (nodes == null) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label("КАРТА КРАЯ", GUILayout.ExpandWidth(true));
            var previousGuiEnabled = GUI.enabled;
            GUI.enabled = previousGuiEnabled && _regionMapZoom > 1f;
            if (GUILayout.Button("−", GUILayout.Width(34f)))
                _regionMapZoom = Mathf.Max(1f, _regionMapZoom - 0.5f);
            GUI.enabled = previousGuiEnabled && _regionMapZoom < 4f;
            if (GUILayout.Button("+", GUILayout.Width(34f)))
                _regionMapZoom = Mathf.Min(4f, _regionMapZoom + 0.5f);
            GUI.enabled = previousGuiEnabled && _regionMapZoom > 1f;
            if (GUILayout.Button("Весь край", GUILayout.Width(88f)))
                _regionMapZoom = 1f;
            GUI.enabled = previousGuiEnabled;
            GUILayout.EndHorizontal();
            GUILayout.Label(_regionMapZoom <= 1f
                ? "Все известные места и дороги"
                : "Приближение к окрестностям текущего поселения · ×" + _regionMapZoom.ToString("0.0", CultureInfo.InvariantCulture));
            DrawRegionMap(nodes, roads, hereId);
            foreach (var node in nodes)
            {
                if (node == null || node.State == "here") continue;
                var title = node.State == "unknown" ? "куда-то ведёт тракт" : node.Name;
                var mark = node.State == "visited" ? "бывал" : node.State == "known" ? "слышал" : "неведомо";
                var length = string.Empty;
                if (roads != null)
                    foreach (var road in roads)
                        if ((road.A == node.Id && road.B == hereId) || (road.B == node.Id && road.A == hereId))
                            length = " · " + (int)road.Length + " вёрст";
                if (GUILayout.Button(title + "  (" + mark + length + ")"))
                {
                    Ask("Дорога", "journey", "target", node.Id);
                    _menuOpen = false;
                }
            }
        }

        // The regional map is a projection of me.region only. Coordinates and edges are
        // server supplied; absent settlements stay in the fog and unknown nodes are nameless.
        private void DrawRegionMap(IList<RegionNodeFrame> nodes, IList<RegionRoadFrame> roads, string hereId)
        {
            var rect = GUILayoutUtility.GetRect(420f, 220f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            var inner = new Rect(rect.x + 18f, rect.y + 24f, rect.width - 36f, rect.height - 42f);

            // The map lives inside a scrollable IMGUI panel. Consume the wheel only while
            // it is over the map so the parent panel does not scroll instead of zooming.
            var currentEvent = Event.current;
            if (currentEvent != null && currentEvent.type == EventType.ScrollWheel &&
                inner.Contains(currentEvent.mousePosition))
            {
                _regionMapZoom = Mathf.Clamp(
                    _regionMapZoom + (currentEvent.delta.y < 0f ? 0.25f : -0.25f), 1f, 4f);
                currentEvent.Use();
            }

            var points = new Dictionary<string, Vector2>();
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var node in nodes)
            {
                var id = node.Id;
                var x = (float)node.X;
                var y = (float)node.Y;
                points[id] = new Vector2(x, y);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            if (points.Count == 0) return;

            const float pad = 18f;
            var spanX = Mathf.Max(1f, maxX - minX);
            var spanY = Mathf.Max(1f, maxY - minY);
            var fitScale = Mathf.Min(
                Mathf.Max(1f, inner.width - pad * 2f) / spanX,
                Mathf.Max(1f, inner.height - pad * 2f) / spanY);
            var boundsCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            var mapCenter = boundsCenter;
            if (_regionMapZoom > 1f)
            {
                foreach (var node in nodes)
                {
                    if (node.Id != hereId) continue;
                    mapCenter = new Vector2((float)node.X, (float)node.Y);
                    break;
                }
            }

            var scale = fitScale * _regionMapZoom;
            var screenCenter = new Vector2(inner.width * 0.5f, inner.height * 0.5f);
            Vector2 ProjectPoint(Vector2 worldPoint)
            {
                return new Vector2(screenCenter.x + (worldPoint.x - mapCenter.x) * scale,
                    screenCenter.y - (worldPoint.y - mapCenter.y) * scale);
            }
            Vector2 Project(RegionNodeFrame node)
            {
                return ProjectPoint(new Vector2((float)node.X, (float)node.Y));
            }

            // Group clips zoomed nodes and roads to the parchment map viewport.
            GUI.BeginGroup(inner);
            if (roads != null)
            {
                foreach (var road in roads)
                {
                    Vector2 worldA, worldB;
                    if (!points.TryGetValue(road.A, out worldA) ||
                        !points.TryGetValue(road.B, out worldB)) continue;
                    var a = ProjectPoint(worldA);
                    var b = ProjectPoint(worldB);
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
                var state = node.State;
                var point = Project(node);
                var previous = GUI.color;
                GUI.color = state == "here" ? new Color(0.95f, 0.78f, 0.35f)
                    : state == "visited" ? new Color(0.65f, 0.78f, 0.55f)
                    : state == "known" ? new Color(0.58f, 0.7f, 0.83f)
                    : new Color(0.55f, 0.53f, 0.48f);
                GUI.DrawTexture(new Rect(point.x - 5f, point.y - 5f, 10f, 10f), Texture2D.whiteTexture);
                GUI.color = previous;
                var label = state == "unknown" ? "?" : node.Name;
                if (state == "here") label = "Ты";
                if (!string.IsNullOrEmpty(label))
                    GUI.Label(new Rect(point.x + 7f, point.y - 10f, 130f, 20f), label);
            }
            GUI.EndGroup();
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
