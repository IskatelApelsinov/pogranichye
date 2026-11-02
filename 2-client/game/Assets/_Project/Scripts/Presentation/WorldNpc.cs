using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Selectable nearby NPC. Selection requests the existing generic socialize goal;
    /// it does not imply a server-side targeted dialogue contract.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WorldNpc : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Renderer _bodyRenderer;
        private Color _baseColor;
        private MaterialPropertyBlock _block;

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
            }
        }

        private void OnMouseEnter()
        {
            if (_bodyRenderer != null)
                ApplyColor(Color.Lerp(_baseColor, Color.white, 0.32f));
        }

        private void OnMouseExit()
        {
            if (_bodyRenderer != null)
                ApplyColor(_baseColor);
        }

        private void OnMouseDown()
        {
            Selected?.Invoke(this);
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
