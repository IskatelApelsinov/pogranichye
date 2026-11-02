using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Selectable nearby NPC. Interaction currently requests only generic socialize;
    /// no targeted dialogue contract exists in v1.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WorldNpc : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Renderer _bodyRenderer;
        private Color _baseColor;
        private MaterialPropertyBlock _block;
        private bool _focused;

        public static event Action<WorldNpc> Selected;
        public ulong EntityId { get; private set; }
        public string DisplayName { get; private set; }

        public void Initialize(ulong entityId, string displayName, Renderer bodyRenderer, Color bodyColor)
        {
            EntityId = entityId;
            DisplayName = displayName ?? "Житель";
            _bodyRenderer = bodyRenderer;
            if (_bodyRenderer != null)
            {
                _baseColor = bodyColor;
                _block = new MaterialPropertyBlock();
                ApplyColor(_baseColor);
            }
        }

        public void RequestInteraction()
        {
            if (enabled) Selected?.Invoke(this);
        }

        /// <summary>Highlights the NPC currently under the interaction reticle.</summary>
        public void SetFocused(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            ApplyColor(_focused ? Color.Lerp(_baseColor, Color.white, 0.38f) : _baseColor);
        }

        private void OnDisable()
        {
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
