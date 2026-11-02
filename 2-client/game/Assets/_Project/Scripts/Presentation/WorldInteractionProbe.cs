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
        [SerializeField] private float interactRange = 3.2f;
        [SerializeField] private KeyCode interactKey = KeyCode.E;
        private Camera _camera;
        private WorldPoi _poi;
        private WorldNpc _npc;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void Update()
        {
            WorldPoi nextPoi = null;
            WorldNpc nextNpc = null;

            if (_camera != null)
            {
                if (Physics.Raycast(_camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)),
                    out var hit, interactRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    nextPoi = hit.collider.GetComponentInParent<WorldPoi>();
                    if (nextPoi == null)
                        nextNpc = hit.collider.GetComponentInParent<WorldNpc>();
                }
            }

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

            if (Input.GetKeyDown(interactKey))
            {
                if (_poi != null) _poi.RequestInteraction();
                else if (_npc != null) _npc.RequestInteraction();
            }
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
            if (_poi == null && _npc == null) return;

            var label = _poi != null
                ? _poi.Title + "  [E]"
                : _npc.DisplayName + "  [E] — поговорить";
            var width = Mathf.Min(420f, Screen.width - 32f);
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.72f, width, 34f);
            GUI.Box(rect, label);
        }
    }
}
