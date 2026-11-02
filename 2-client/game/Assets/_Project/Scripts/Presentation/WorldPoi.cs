using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Clickable tutorial point. Selection requests an existing server goal; it does not
    /// perform the activity or mutate world state locally.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WorldPoi : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Renderer _visual;
        private Color _baseColor;
        private MaterialPropertyBlock _propertyBlock;

        public static event Action<WorldPoi> Selected;

        public string Title { get; private set; }
        public string Goal { get; private set; }

        public void Initialize(string title, string goal, Renderer visual, Color color)
        {
            Title = title;
            Goal = goal;
            _visual = visual;
            _baseColor = color;
            _propertyBlock = new MaterialPropertyBlock();
            ApplyColor(_baseColor);
        }

        private void OnMouseEnter()
        {
            if (enabled && _visual != null)
                ApplyColor(Color.Lerp(_baseColor, Color.white, 0.55f));
        }

        private void OnMouseExit()
        {
            if (_visual != null)
                ApplyColor(_baseColor);
        }

        private void OnMouseDown()
        {
            if (enabled && !string.IsNullOrEmpty(Goal))
                Selected?.Invoke(this);
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
