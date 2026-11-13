using UnityEngine;
using UnityEngine.Rendering;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Lightweight distance-based presentation LOD for replicated entities.
    /// It never changes replication, identity, interaction range, or gameplay state.
    /// </summary>
    public sealed class EntityViewLod : MonoBehaviour
    {
        private const float HideLabelDistance = 24f;
        private const float ShowLabelDistance = 21f;
        private const float DisableShadowsDistance = 18f;
        private const float EnableShadowsDistance = 15f;

        private GameObject _label;
        private Renderer[] _renderers;
        private bool _labelVisible = true;
        private bool _shadowsEnabled = true;

        public void Initialize(GameObject label, Renderer[] renderers)
        {
            _label = label;
            _renderers = renderers;
        }

        public void ApplyDistance(float distance)
        {
            if (_label != null)
            {
                if (_labelVisible && distance >= HideLabelDistance)
                {
                    _labelVisible = false;
                    _label.SetActive(false);
                }
                else if (!_labelVisible && distance <= ShowLabelDistance)
                {
                    _labelVisible = true;
                    _label.SetActive(true);
                }
            }

            if (_shadowsEnabled && distance >= DisableShadowsDistance)
            {
                _shadowsEnabled = false;
                SetShadowMode(false);
            }
            else if (!_shadowsEnabled && distance <= EnableShadowsDistance)
            {
                _shadowsEnabled = true;
                SetShadowMode(true);
            }
        }

        private void SetShadowMode(bool enabled)
        {
            if (_renderers == null) return;
            for (var i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;
                renderer.shadowCastingMode = enabled
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = enabled;
            }
        }
    }
}
