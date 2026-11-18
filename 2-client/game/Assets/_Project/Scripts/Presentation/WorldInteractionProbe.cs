using UnityEngine.Serialization;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Short-range first-person interaction probe. It only selects a presentation target;
    /// the controller decides which already-supported intent, if any, may be sent.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class WorldInteractionProbe : MonoBehaviour
    {
        [FormerlySerializedAs("interactRange")]
        [SerializeField] private float _interactRange = 3.2f;
        [FormerlySerializedAs("aimAssistRadius")]
        [SerializeField] private float _aimAssistRadius = 0.18f;
        private const int HitCapacity = 32;
        private readonly RaycastHit[] _hits = new RaycastHit[HitCapacity];
        // Building shells and tavern wall segments intentionally use Unity's Ignore Raycast layer (2)
        // for the camera rig. Include that layer here so solid walls still occlude E/Interact.
        private static readonly int InteractionRaycastMask =
            WorldInteractionRaycastMaskPolicy.IncludeIgnoreRaycastLayer(Physics.DefaultRaycastLayers);
        private Camera _camera;
        private PresentationCameraRig _cameraRig;
        private Pogranichye.Client.ClientInput _input;
        private Pogranichye.Client.CoreGameplay _core;
        private WorldPoi _poi;
        private WorldNpc _npc;
        private static GUIStyle _promptStyle;
        private static int _promptStyleScreenHeight;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _cameraRig = GetComponent<PresentationCameraRig>();
        }

        public void BindInput(Pogranichye.Client.ClientInput input, Pogranichye.Client.CoreGameplay core)
        {
            _input = input;
            _core = core;
        }

        private void Update()
        {
            ResolveTarget(out var nextPoi, out var nextNpc);

            if (_poi != nextPoi)
            {
                if (_poi != null) _poi.SetFocused(false);
                _poi = nextPoi;
                if (_poi != null) _poi.SetFocused(true);
            }

            if (_npc != nextNpc)
            {
                if (_npc != null) _npc.SetFocused(false);
                _npc = nextNpc;
                if (_npc != null) _npc.SetFocused(true);
            }

            // World focus is presentation-only. Do not let E leak through a character menu,
            // weave wheel or modal window that currently owns gameplay input.
            if (_input != null && _input.InteractPressed && (_core == null || (!_core.CapturesInput && !_core.ConsumesWorldInteraction)))
            {
                if (_poi != null) _poi.RequestInteraction();
                else if (_npc != null) _npc.RequestInteraction();
            }
        }

        private void ResolveTarget(out WorldPoi poi, out WorldNpc npc)
        {
            poi = null;
            npc = null;
            if (_camera == null) return;

            var ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            // Include non-blocking POI/NPC trigger volumes. FindTargetBeforeObstacle skips
            // unrelated triggers but still stops at the first solid wall/door collider.
            var count = Physics.RaycastNonAlloc(ray, _hits, _interactRange,
                InteractionRaycastMask, QueryTriggerInteraction.Collide);

            // Unity may return an arbitrary subset when a NonAlloc buffer is full. If
            // we sorted that subset and selected a target, a nearer wall could be missing.
            // Fail closed rather than allowing E to interact through dense geometry.
            if (count >= _hits.Length) return;
            SortHits(count);
            if (FindTargetBeforeObstacle(count, out poi, out npc)) return;

            // A small, bounded sphere sweep makes E interaction forgiving for narrow NPC
            // colliders and low-poly POI meshes. Solid world geometry still occludes targets.
            if (_aimAssistRadius <= 0f) return;
            count = Physics.SphereCastNonAlloc(ray, _aimAssistRadius, _hits, _interactRange,
                InteractionRaycastMask, QueryTriggerInteraction.Collide);
            if (count >= _hits.Length) return;
            SortHits(count);
            FindTargetBeforeObstacle(count, out poi, out npc);
        }

        private void SortHits(int count)
        {
            // NonAlloc queries do not promise hit ordering. Insertion sort is cheap for the
            // small fixed buffer and avoids per-frame allocations from RaycastAll/Array.Sort.
            for (var i = 1; i < count; i++)
            {
                var value = _hits[i];
                var j = i - 1;
                while (j >= 0 && _hits[j].distance > value.distance)
                {
                    _hits[j + 1] = _hits[j];
                    j--;
                }
                _hits[j + 1] = value;
            }
        }

        private bool FindTargetBeforeObstacle(int count, out WorldPoi poi, out WorldNpc npc)
        {
            poi = null;
            npc = null;
            for (var i = 0; i < count; i++)
            {
                var collider = _hits[i].collider;
                if (collider == null || !collider.enabled) continue;

                var hitPoi = collider.GetComponentInParent<WorldPoi>();
                var hitNpc = hitPoi == null ? collider.GetComponentInParent<WorldNpc>() : null;
                if (hitPoi != null || hitNpc != null)
                {
                    poi = hitPoi;
                    npc = hitNpc;
                    return true;
                }

                // Ignore triggers and non-blocking colliders; the first solid collider
                // prevents selecting an NPC/POI behind a wall, building or closed door.
                if (!collider.isTrigger) return false;
            }
            return false;
        }

        private void OnDisable()
        {
            if (_poi != null) _poi.SetFocused(false);
            if (_npc != null) _npc.SetFocused(false);
            _poi = null;
            _npc = null;
        }

        private void OnGUI()
        {
            // SetupPresentationScene may add this probe immediately before it adds the
            // camera rig. Awake then runs while no rig exists, so lazily bind it here.
            if (_cameraRig == null)
                _cameraRig = GetComponent<PresentationCameraRig>();

            var firstPerson = _cameraRig != null && _cameraRig.IsFirstPerson;
            var hasTarget = _poi != null || _npc != null;

            if (firstPerson)
                DrawReticle(hasTarget);

            if (!hasTarget) return;

            var label = _poi != null
                ? (_poi.CanInteract
                    ? _poi.Title + (string.IsNullOrWhiteSpace(_poi.Hint) ? string.Empty : "\n" + _poi.Hint) + "   [E]"
                    : _poi.Title + (string.IsNullOrWhiteSpace(_poi.UnavailableReason)
                        ? "\nСейчас недоступно" : "\n" + _poi.UnavailableReason))
                : "Поговорить: " + (string.IsNullOrWhiteSpace(_npc.DisplayName) ? "житель" : _npc.DisplayName) + "   [E]";
            var width = Mathf.Min(560f, Mathf.Max(180f, Screen.width - 32f));
            EnsurePromptStyle();
            var content = new GUIContent(label);
            var height = Mathf.Max(42f, _promptStyle.CalcHeight(content, width - 24f) + 10f);
            var rect = new Rect((Screen.width - width) * 0.5f,
                Mathf.Max(12f, Screen.height - 112f - height), width, height);

            var previousColor = GUI.color;
            GUI.color = new Color(0.10f, 0.085f, 0.065f, 0.92f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = new Color(0.96f, 0.88f, 0.70f, 1f);
            GUI.Label(rect, content, _promptStyle);
            GUI.color = previousColor;
        }

        private static void EnsurePromptStyle()
        {
            if (_promptStyle != null && _promptStyleScreenHeight == Screen.height) return;
            _promptStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = ClientUiScalePolicy.ResolveFontSize(ClientUiTypography.HudBody, Screen.height),
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(12, 12, 7, 7)
            };
            _promptStyle.normal.textColor = new Color(0.96f, 0.88f, 0.70f, 1f);
            _promptStyleScreenHeight = Screen.height;
        }

        private static void DrawReticle(bool focused)
        {
            var previousColor = GUI.color;
            var centerX = Screen.width * 0.5f;
            var centerY = Screen.height * 0.5f;
            var color = focused
                ? new Color(1f, 0.78f, 0.38f, 0.95f)
                : new Color(0.92f, 0.91f, 0.84f, 0.72f);
            GUI.color = color;

            GUI.DrawTexture(new Rect(centerX - 1f, centerY - 7f, 2f, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(centerX - 1f, centerY + 3f, 2f, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(centerX - 7f, centerY - 1f, 4f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(centerX + 3f, centerY - 1f, 4f, 2f), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }
}