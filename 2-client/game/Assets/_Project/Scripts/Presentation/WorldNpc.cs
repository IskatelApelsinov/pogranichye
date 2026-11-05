using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Selectable nearby NPC. Identity and dialogue feedback are driven by server snapshots.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WorldNpc : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Renderer _bodyRenderer;
        private Color _baseColor;
        private MaterialPropertyBlock _block;
        private EntityMotion _motion;
        private bool _focused;

        public static event Action<WorldNpc> Selected;
        public ulong EntityId { get; private set; }
        public string DisplayName { get; private set; }

        public void Initialize(ulong entityId, string displayName, Renderer bodyRenderer, Color bodyColor)
        {
            EntityId = entityId;
            DisplayName = displayName ?? "Житель";
            _bodyRenderer = bodyRenderer;
            _motion = GetComponent<EntityMotion>();
            if (_bodyRenderer != null)
            {
                _baseColor = bodyColor;
                _block = new MaterialPropertyBlock();
                ApplyColor(_baseColor);
            }
        }

        public void RequestInteraction()
        {
            if (!enabled) return;

            Selected?.Invoke(this);
        }

        public void SetServerIdentity(bool known, string name, string description)
        {
            DisplayName = known && !string.IsNullOrWhiteSpace(name)
                ? name
                : (string.IsNullOrWhiteSpace(description) ? "Житель" : description);
        }

        public void PlayServerTalk(Transform observer)
        {
            if (_motion != null) _motion.PlayTalkGesture(observer);
        }

        public void PlayServerClink()
        {
            if (_motion != null) _motion.PlayClinkGesture();
        }

        /// <summary>Highlights the NPC currently under the interaction reticle.</summary>
        public void SetFocused(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            if (_motion != null)
                _motion.SetFocusTarget(_focused && Camera.main != null ? Camera.main.transform : null);
            ApplyColor(_focused ? Color.Lerp(_baseColor, Color.white, 0.38f) : _baseColor);
        }

        private void OnDisable()
        {
            // The probe may be disabled during disconnect or scene teardown. Do not leave
            // a stale camera Transform on an NPC that is no longer the active focus target.
            if (_motion != null) _motion.SetFocusTarget(null);
            if (_focused)
            {
                _focused = false;
                ApplyColor(_baseColor);
            }
        }

        private void ApplyColor(Color color)
        {
            if (_bodyRenderer == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            _bodyRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            _bodyRenderer.SetPropertyBlock(_block);
        }
    }
}