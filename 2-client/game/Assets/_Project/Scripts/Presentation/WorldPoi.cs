using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Server-backed interaction target selected through the short-range probe.
    /// It only forwards an existing server intent; it never performs gameplay locally.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WorldPoi : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Renderer _visual;
        private Color _baseColor;
        private MaterialPropertyBlock _propertyBlock;
        private bool _focused;

        public static event Action<WorldPoi> Selected;
        public string Title { get; private set; }
        public string Goal { get; private set; }
        public string ActionIntent { get; private set; }
        public string InteractionId { get; private set; }
        public string Hint { get; private set; }
        public string UnavailableReason { get; private set; }
        public string TargetType { get; private set; }
        public string TargetId { get; private set; }
        public bool CanInteract { get; private set; } = true;

        public void Initialize(string title, string goal, Renderer visual, Color color, string action = null,
            string interactionId = null, string hint = null, bool canInteract = true, string unavailableReason = null,
            string targetType = null, string targetId = null)
        {
            Title = title;
            Goal = goal;
            ActionIntent = action;
            InteractionId = interactionId;
            Hint = hint;
            CanInteract = canInteract;
            UnavailableReason = unavailableReason;
            TargetType = targetType;
            TargetId = targetId;
            _visual = visual;
            _baseColor = color;
            _propertyBlock = new MaterialPropertyBlock();
            ApplyColor(_baseColor);
        }

        public void RequestInteraction()
        {
            if (enabled && CanInteract && (!string.IsNullOrEmpty(InteractionId) || !string.IsNullOrEmpty(Goal) || !string.IsNullOrEmpty(ActionIntent) || !string.IsNullOrEmpty(TargetId)))
                Selected?.Invoke(this);
        }

        /// <summary>Highlights the POI currently under the interaction reticle.</summary>
        public void SetFocused(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            ApplyColor(_focused ? Color.Lerp(_baseColor, Color.white, 0.62f) : _baseColor);
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
            if (_visual == null) return;
            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();
            _visual.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            _visual.SetPropertyBlock(_propertyBlock);
        }
    }
}
